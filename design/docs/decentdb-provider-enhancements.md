## DecentDB Provider Enhancement Candidates

**Date**: 2026-06-15
**Status**: Updated after DecentDB 2.13.1 validation; DDB-002 and DDB-003
completed

## Context

Melodee now references `DecentDB.AdoNet` `2.13.1`,
`DecentDB.EntityFrameworkCore` `2.13.1`, and
`DecentDB.EntityFrameworkCore.NodaTime` `2.13.1`. The large real-file import
and query timings in `design/DECENTDB_IMPROVEMENTS.md` were rerun against
DecentDB `2.13.0` and then revalidated against DecentDB `2.13.1` on
2026-06-15.

The current DecentDB packages include the post-`2.9.0` fixes for Melodee's
original provider-follow-up items: ordered indexed string equality remains on
the native executor fast path, EF Core has regression coverage for Melodee's
`Where(...).OrderBy(...).Take(...)` query shape, and the ADO.NET bindings
expose native index rebuild helpers. The published `2.13.0` package still did
not satisfy DDB-002 or DDB-003 because checkpointed `Artist` table indexed
equality timed out even with captured `IndexSeek` plans.

The DecentDB `2.13.1` package removes open-time eager all-index hydration and
adds a bounded process cache for deferred runtime B-tree indexes plus paged row
locators. Melodee validation against the real checkpointed MusicBrainz file now
shows warm `Artist.NameNormalized`, `ArtistAlias.NameNormalized`, and
`Artist.MusicBrainzIdRaw` equality paths under `1 ms` with `IndexSeek`.

This document is still needed as provider context for future DecentDB package
regression checks. The Melodee side now has repeatable manual probes:

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

## Melodee Package-Upgrade Validation Gate

Use the new internal runbook and helper for package-upgrade checks:

- `design/docs/decentdb-package-upgrade-validation-runbook.md`
- `scripts/run-decentdb-package-upgrade-gate.sh`

The helper performs:

- optional fresh import probe capture (from staging data)
- checkpointed query probe capture against an existing `.ddb`
- DDB-002/DDB-003 warm-query gate checks for `IndexSeek` + request-safe timings
- optional explicit sample reuse (`--name`, `--alias`, `--mbid`) so package
  validation can avoid broad sampling work
- consistent output layout for attachment to future provider regressions

This is a Melodee-only gating helper; it does not alter production workloads
or depend on DecentDB CLI usage.

These probes give reproducible inputs for provider issues without requiring
ad hoc scripts.

## Melodee Warm-Up And Request-Path Guardrails

Melodee now warms the large MusicBrainz DecentDB indexes through native .NET
queries after Blazor startup and after a successful MusicBrainz database
promotion. The warm-up is opportunistic and non-fatal; if it cannot complete,
search remains available and the same indexes warm on demand.

The warmed query shapes intentionally match request-safe repository behavior:

- exact `Artist.NameNormalized` equality
- exact `Artist.MusicBrainzIdRaw` equality
- exact `ArtistAlias.NameNormalized` equality
- bounded albums by `MusicBrainzArtistId`

The broad `ordered-first-row-existence` measurement remains available for
investigation with `musicbrainz-query-probe --include-row-existence`, but it is
not part of normal package gate acceptance.

## Enhancement List

Items marked published below are available to Melodee through the DecentDB
`2.13.1` .NET bindings.

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

- The DecentDB `2.13.0` real-file import finished with the main `.ddb` file at
  `8 KB` and the WAL at roughly `5.0 GiB`.
- Native ADO.NET checkpoint reduced the WAL to `32 B` and grew the main `.ddb`
  file to roughly `2.4 GiB`, but the checkpoint took about `636 s`.

Requested enhancement:

- Implemented in DecentDB `2.9.0`: `DecentDBMaintenance.GetWalStatus(...)`
  and checkpoint results expose before/after WAL sizes through ADO.NET.
- Document `WalAutoCheckpoint` behavior and the connection string settings that
  affect large import workloads.

### Indexed String Equality Performance

Current observation from the published DecentDB `2.13.0` rebuilt and
checkpointed MusicBrainz file:

- `Artist.NameNormalized == value` targeted probe: timed out after `180 s`.
- `Artist.MusicBrainzIdRaw == value` targeted probe: timed out after `180 s`.
- The affected columns have EF model indexes, generated SQL is captured by the
  query probe, and DecentDB `EXPLAIN` reports `IndexSeek` for the tested
  equality shapes.
- The remaining package issue is not index selection: the probe captures
  `IndexSeek` plans while execution does not complete in a request-safe window
  on the checkpointed large `Artist` table.

DecentDB `2.13.1` package observation:

- `Database.CanConnectAsync()`: `16.57 ms` cold.
- `Artist.NameNormalized`: `9,996 ms` cold first-use hydration and `0.66 ms`
  warm.
- `ArtistAlias.NameNormalized`: `780 ms` cold and `0.80 ms` warm.
- `Artist.MusicBrainzIdRaw`: `11.04 ms` cold and `0.56 ms` warm.
- All three indexed equality plans use `IndexSeek`.

Requested enhancement:

- Published in DecentDB `2.9.0`: .NET provider regression coverage for indexed
  string equality translation.
- Published in DecentDB `2.10.0`: the native executor accepts simple
  `ORDER BY`, `LIMIT`, and `OFFSET` clauses for indexed equality projections,
  and EF Core regression coverage executes Melodee's
  `Where(...).OrderBy(...).Take(1)` shape.
- Published in DecentDB `2.13.1`: DecentDB keeps deferred-table open lazy,
  hydrates only the requested secondary index on first use, and reuses that
  runtime index plus paged row locators across short-lived connections in the
  same process.

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
