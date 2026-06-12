## DecentDB Provider Enhancement Candidates

**Date**: 2026-06-12
**Status**: Updated after DecentDB 2.11.0 validation; provider follow-up still
needed for large `Artist` table indexed equality execution

## Context

Melodee now references `DecentDB.AdoNet` `2.11.0`,
`DecentDB.EntityFrameworkCore` `2.11.0`, and
`DecentDB.EntityFrameworkCore.NodaTime` `2.11.0`. The large real-file import and
query timings in `design/DECENTDB_IMPROVEMENTS.md` were rerun against
DecentDB `2.11.0` on 2026-06-12.

The current DecentDB packages include the post-`2.9.0` fixes for Melodee's
original provider-follow-up items: ordered indexed string equality remains on
the native executor fast path, EF Core has regression coverage for Melodee's
`Where(...).OrderBy(...).Take(...)` query shape, and the ADO.NET bindings
expose native index rebuild helpers. The real-file validation still did not
satisfy DDB-002 or DDB-003 because checkpointed `Artist` table indexed equality
remains multi-second even with captured `IndexSeek` plans.

This document is still needed because `design/DECENTDB_IMPROVEMENTS.md` uses it
as the release-validation detail for DDB-002 and DDB-003. The Melodee side now
has repeatable manual probes:

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

Items marked published below are available to Melodee through the DecentDB
`2.11.0` .NET bindings. The remaining item is runtime/storage performance for
large checkpointed `Artist` table indexed equality, not EF translation.

### Native .NET Checkpoint And Maintenance APIs

Status:

- Published in DecentDB `2.9.0`: checkpoint, vacuum, compact, and WAL-status
  helpers.
- Published in DecentDB `2.10.0`: binding-native index rebuild helpers.
- Melodee now checkpoints imported MusicBrainz databases through
  `DecentDBMaintenance.CheckpointAsync(...)` and does not execute an external
  DecentDB process.

Useful API shapes added upstream:

```csharp
await DecentDBMaintenance.CheckpointAsync(databasePath, cancellationToken);
await DecentDBMaintenance.VacuumAsync(databasePath, cancellationToken);
await DecentDBMaintenance.CompactAsync(sourcePath, targetPath, cancellationToken);
await DecentDBMaintenance.RebuildIndexAsync(databasePath, indexName, cancellationToken);
await DecentDBMaintenance.RebuildIndexesAsync(databasePath, cancellationToken);
```

The rebuild helpers give Melodee a native maintenance surface if future
validation proves that a targeted rebuild workflow is useful, but they did not
make DDB-002 or DDB-003 complete by themselves.

### WAL Checkpoint Visibility

Current observation:

- The DecentDB `2.11.0` real-file import finished with the main `.ddb` file at
  `8 KB` and the WAL at roughly `5.0 GiB`.
- Native ADO.NET checkpoint reduced the WAL to `32 B` and grew the main `.ddb`
  file to roughly `2.4 GiB`, but the checkpoint took about `561 s`.

Requested enhancement:

- Implemented in DecentDB `2.9.0`: `DecentDBMaintenance.GetWalStatus(...)`
  and checkpoint results expose before/after WAL sizes through ADO.NET.
- Document `WalAutoCheckpoint` behavior and the connection string settings that
  affect large import workloads.

### Indexed String Equality Performance

Current observation from the DecentDB `2.11.0` rebuilt and checkpointed
MusicBrainz file:

- `Artist.NameNormalized == value` warm lookup: about `3.4 s`.
- `Artist.MusicBrainzIdRaw == value` warm lookup: about `3.4 s`.
- `ArtistAlias.NameNormalized == value` warm lookup: about `257 ms`.
- The affected columns have EF model indexes, generated SQL is captured by the
  query probe, and DecentDB `EXPLAIN` reports `IndexSeek` for all three
  equality shapes.
- A raw ADO.NET probe without `ORDER BY` still shows large `Artist` table
  indexed equality around `3.3 s`, so the remaining issue is not Melodee's EF
  query shape or ordering.

Requested enhancement:

- Published in DecentDB `2.9.0`: .NET provider regression coverage for indexed
  string equality translation.
- Published in DecentDB `2.10.0`: the native executor accepts simple
  `ORDER BY`, `LIMIT`, and `OFFSET` clauses for indexed equality projections,
  and EF Core regression coverage executes Melodee's
  `Where(...).OrderBy(...).Take(1)` shape.
- Remaining follow-up: reduce checkpointed large-table indexed equality
  execution time after `IndexSeek` has already been selected. This should be
  investigated below EF Core as runtime/storage/index payload lookup cost.

### Query Plan And Index Diagnostics

Current observation:

- `ToQueryString()` gives useful SQL shape for EF queries.
- Melodee's manual query probe now records both EF-generated SQL and DecentDB
  `EXPLAIN` output for the fixed probe shapes.

Requested enhancement:

- Implemented in DecentDB `2.9.0`: ADO.NET exposes an opt-in query-plan helper.
- Implemented in Melodee: `musicbrainz-query-probe` records generated SQL,
  elapsed time, row counts, sample values, EF model index metadata, and
  structured plan diagnostics from the DecentDB ADO.NET query-plan helper.
- Keep plan diagnostics opt-in and safe for app-level probe output.

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
