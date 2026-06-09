## DecentDB Provider Enhancement Candidates

**Date**: 2026-06-09
**Status**: Needed for DecentDB provider follow-up after DecentDB `2.9.0`

## Context

Melodee now references `DecentDB.AdoNet` `2.9.0`,
`DecentDB.EntityFrameworkCore` `2.9.0`, and
`DecentDB.EntityFrameworkCore.NodaTime` `2.9.0`. The large real-file import and
query timings in `design/DECENTDB_IMPROVEMENTS.md` were validated against
`DecentDB.EntityFrameworkCore` `2.8.0` and
`DecentDB.EntityFrameworkCore.NodaTime` `2.8.0`, and should be treated as a
historical baseline until the probes are rerun.

This document is still needed because `design/DECENTDB_IMPROVEMENTS.md` uses it
as the provider-facing detail for DDB-003. The Melodee side now has repeatable
manual probes:

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks \
  -- musicbrainz-import-probe \
  --storage /path/to/musicbrainz-storage \
  --db /tmp/musicbrainz.ddb \
  --output /tmp/musicbrainz-import-probe.json \
  --clean
```

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks \
  -- musicbrainz-query-probe \
  --db /tmp/musicbrainz.ddb \
  --output /tmp/musicbrainz-query-probe.json
```

These probes give reproducible inputs for provider issues without requiring
ad hoc scripts.

## Enhancement List

Items marked implemented below are complete in the DecentDB `2.9.0` .NET
bindings. They remain here as context for Melodee's current dependency
baseline and for deciding whether future DecentDB provider work has closed the
large-file indexed equality follow-up.

### Native .NET Checkpoint And Maintenance APIs

Status:

- Published in DecentDB `2.9.0`.
- Melodee now checkpoints imported MusicBrainz databases through
  `DecentDBMaintenance.CheckpointAsync(...)` and does not execute an external
  DecentDB process.

Useful API shapes added upstream:

```csharp
await DecentDBMaintenance.CheckpointAsync(databasePath, cancellationToken);
await DecentDBMaintenance.VacuumAsync(databasePath, cancellationToken);
await DecentDBMaintenance.CompactAsync(sourcePath, targetPath, cancellationToken);
```

### WAL Checkpoint Visibility

Current observation:

- A clean MusicBrainz import finished with the main `.ddb` file at `8 KB` and
  the WAL at roughly `5.1 GB`.
- That historical run happened before binding-native file maintenance helpers
  were available.

Requested enhancement:

- Implemented in DecentDB `2.9.0`: `DecentDBMaintenance.GetWalStatus(...)`
  and checkpoint results expose before/after WAL sizes through ADO.NET.
- Document `WalAutoCheckpoint` behavior and the connection string settings that
  affect large import workloads.

### Indexed String Equality Performance

Current observation from the rebuilt MusicBrainz file:

- `Artist.NameNormalized == value` warm lookup: about `7.6 s`.
- `Artist.MusicBrainzIdRaw == value` warm lookup: about `7.6 s`.
- `ArtistAlias.NameNormalized == value` warm lookup: about `5.7 s`.
- The affected columns have EF model indexes and generated SQL is captured by
  the query probe.

Requested enhancement:

- DecentDB `2.9.0` adds .NET provider regression coverage for indexed string
  equality translation.
- Verify parameter binding, string storage encoding, index selection, and
  reader round-tripping at large row counts.
- Provide guidance for whether WAL-backed, uncheckpointed files are expected
  to have materially different lookup performance.
- Rerun the Melodee real-file query probe after a native planner, storage, or
  index-selection fix is available.

### Query Plan And Index Diagnostics

Current observation:

- `ToQueryString()` gives useful SQL shape for EF queries.
- Melodee still cannot tell whether DecentDB used an index, scanned rows, or
  read from WAL-heavy storage for a slow lookup.

Requested enhancement:

- Implemented in DecentDB `2.9.0`: ADO.NET exposes an opt-in query-plan helper.
- Melodee's current `musicbrainz-query-probe` records generated SQL, elapsed
  time, row counts, sample values, and EF model index metadata. It does not yet
  call the DecentDB ADO.NET query-plan helper.
- Keep any future plan diagnostics opt-in and safe for app-level probe output.

### Large Text Search Guidance Or Full-Text Support

Current observation:

- Melodee removed substring scans from large request paths and uses normalized
  lookup tables instead.
- This is safe, but it pushes fuzzy search responsibility to application
  schema design.

Requested enhancement:

- Document DecentDB's recommended pattern for large substring or fuzzy text
  workloads.
- If provider support is planned, expose token/full-text indexes with clear
  query-shape guidance.

### Database Format Compatibility Diagnostics

Current observation:

- An existing MusicBrainz database failed to open with
  `unsupported database format version: 11` after the DecentDB `2.8.0` upgrade.

Requested enhancement:

- DecentDB `2.9.0` improves the ADO.NET open failure message for unsupported
  file format versions.
- Provide clearer upgrade guidance for old file format versions.
- Expose a supported upgrade or migration path when possible.
