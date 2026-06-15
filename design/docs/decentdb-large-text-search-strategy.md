## DecentDB Large-Text Search Strategy

**Date**: 2026-06-15
**Status**: Needed and adopted for Melodee DecentDB-backed request paths

## Decision

Melodee will not use substring scans over large text or blob-like columns on
normal DecentDB-backed request paths. Large text search must use one of these
bounded shapes:

- exact equality against indexed normalized columns
- exact equality against dedicated normalized lookup tables
- small, explicitly bounded fallback queries that are kept off hot request
  paths
- a separate full-text or token index if fuzzy search becomes a product
  requirement

This is a Melodee schema and query-shape decision. DecentDB provider full-text
support may be useful in the future, but current Melodee scale work must not
depend on provider substring optimization for request safety.

This document is still needed because `design/DECENTDB_IMPROVEMENTS.md` uses it
as the adopted guidance for DDB-009.

## Rationale

Real-file MusicBrainz probes showed that substring scans over alternate-name
text are not viable on large request paths. The MusicBrainz search path follows
the safer schema shape by using normalized artist names and the `ArtistAlias`
lookup table instead of scanning an `AlternateNames` string.

The published DecentDB `2.13.0` real-file equality timings were still too slow
for hot request paths on the large `Artist` table. DDB-002 and DDB-003 consumed
the published planner/provider fixes, and DecentDB `EXPLAIN` reported
`IndexSeek`, but checkpointed warm `Artist.NameNormalized` and
`Artist.MusicBrainzIdRaw` targeted probes timed out after `180 s`.

DecentDB `2.13.1` has since validated the indexed equality paths through the
published NuGet packages. This strategy document still defines the Melodee
query shape that should be preserved for future package updates and
regression checks.

## Query Rules

- Prefer normalized exact-match columns such as `NameNormalized` and
  `MusicBrainzIdRaw`.
- Store alternate names as one normalized row per searchable value when the
  dataset can become large.
- Apply `OrderBy` before `Take`, `Skip`, `First`, or `FirstOrDefault` when the
  result shape is not naturally unique.
- Load related data only after a bounded candidate set is known.
- Do not add automatic `Contains(...)` fallback over large text fields.
- If a fuzzy fallback is required, make it opt-in, bounded, instrumented, and
  absent from normal ingestion or page-render paths.

## Diagnostics

Use the manual MusicBrainz query probe for DecentDB-backed query validation:

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks \
  -- musicbrainz-query-probe \
  --db /path/to/musicbrainz.ddb \
  --output /tmp/musicbrainz-query-probe.json
```

The report includes cold and warm timings, row counts, generated SQL, DecentDB
`EXPLAIN` output for the fixed probe shapes, configured index metadata, and the
sample values used for the exact-name, exact-alias, and exact-MBID probes.

Use the real-file import probe when validating import-scale behavior:

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks \
  -- musicbrainz-import-probe \
  --storage /path/to/musicbrainz-storage \
  --db /tmp/musicbrainz.ddb \
  --output /tmp/musicbrainz-import-probe.json \
  --clean
```

The import report captures phase timings, imported row counts reported by the
streaming importer, process memory, Linux `VmRSS` and `VmHWM` when available,
CPU time, and DecentDB file/WAL growth samples.
