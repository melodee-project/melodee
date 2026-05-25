## DecentDB EntityFramework Improvements for Melodee

**Date**: 2026-03-21
**Last updated**: 2026-05-25
**Status**: Open items only

## Purpose

This document is now an active implementation backlog for the remaining
DecentDB and Melodee scale work. Historical fixes that have already landed were
removed so coding agents can treat the table below as the source of truth for
unfinished work.

Melodee uses `DecentDB.EntityFrameworkCore` for two different workloads:

- `MusicBrainzConnection` backs the large, read-mostly MusicBrainz materialized
  database.
- `ArtistSearchEngineConnection` backs the smaller local artist search cache
  used by the search-engine enrichment workflow.

The large MusicBrainz database can support Melodee when query shapes stay
index-friendly, bounded, and explicit. The remaining work is focused on
validation, regression probes, and the smaller number of query shapes that can
still become expensive at very large scale.

## Status Values

| Status | Meaning |
| --- | --- |
| TODO | Implementation or documentation work is still required. |
| TODO/VERIFY | The code path may exist, but real-data proof is still missing. |
| TODO/CONDITIONAL | Implement after the dependency row proves the work is needed, or while already touching the same area. |

## Active TODO Table

| ID | Status | Priority | Owner | Area | Acceptance target |
| --- | --- | --- | --- | --- | --- |
| DDB-001 | TODO/VERIFY | High | Melodee | Real-file MusicBrainz rebuild | Run a clean monitored rebuild to completion and record final wall-clock, RSS, `VmHWM`, `.ddb`, `.wal`, and post-import probe timings. |
| DDB-002 | TODO/VERIFY | High | Melodee / DecentDB | Exact `MusicBrainzIdRaw` lookup | Prove exact MBID lookup on a rebuilt database is request-safe and uses the indexed string path, or capture a focused repro for provider work. |
| DDB-003 | TODO/CONDITIONAL | High | DecentDB provider | Indexed string equality | If DDB-002 remains slow, isolate provider/data causes for equality on large indexed string columns and add provider regression coverage. |
| DDB-004 | TODO | Medium | Melodee | Local artist cache search | Split local cache search into staged exact-match phases and remove wide mixed `OR` plus substring lookup from the normal fast path. |
| DDB-005 | TODO | Medium | Melodee | Local cache listing | Avoid mandatory `CountAsync()` and full table materialization before pagination for normal local-cache list views. |
| DDB-006 | TODO | Low | Melodee | Import completion counts | Prefer counts already known from import/materialization steps over end-of-job large-table `CountAsync()` probes where possible. |
| DDB-007 | TODO | Medium | Melodee | MusicBrainz query perf probes | Commit an explicit probe for first-row existence, exact normalized name, exact alias, and exact MBID lookup. |
| DDB-008 | TODO | Medium | Melodee | MusicBrainz import perf probes | Commit an import perf harness that captures phase timings, peak memory, and database file growth without including build time. |
| DDB-009 | TODO | Medium | DecentDB provider / Melodee docs | Large-text search strategy | Decide whether substring-heavy workloads need provider support or schema-level guidance that keeps them off large request paths. |
| DDB-010 | TODO | Medium | DecentDB provider / Melodee | Large-file diagnostics | Add practical SQL, timing, and plan diagnostics so slow large-file query shapes are visible during app-level probes. |

## Historical Measurements That Still Drive The TODOs

The following measurements came from a focused probe against a real
MusicBrainz DecentDB file of roughly `2.31 GB`. They remain useful because they
explain why the open items exist.

| Query shape | Prior timing | Current instruction |
| --- | ---: | --- |
| `Database.CanConnectAsync()` | ~3-13 ms | Safe connectivity probe. |
| First-row projection, for example `Select(x => x.Id).FirstOrDefaultAsync()` | ~7-15 ms | Preferred existence pattern for large request paths. |
| `Artists.AnyAsync()` / `Artists.Take(1).CountAsync()` | ~13 s | Keep off request paths and avoid in large-table probes. |
| Exact normalized-name MusicBrainz artist lookup | ~13-40 ms | Keep as the primary large-file search path. |
| `AlternateNames.Contains(...)` substring lookup | ~15 s | Do not use blob substring scans on the large request path. |
| Tokenized `Contains(...)` fallback | ~22 s | Do not reintroduce as an automatic large-file fallback. |
| Exact `MusicBrainzIdRaw` lookup | ~34 s | DDB-002 and DDB-003 must explain or fix this. |

## DDB-001: Clean Monitored Real-File Rebuild

**Status**: TODO/VERIFY

Run the MusicBrainz import against the real dump to completion and record final
numbers. The last known clean run was intentionally stopped before completion,
so the existing partial data is not enough to close the DecentDB work.

Relevant entry points:

- `src/Melodee.Common/Jobs/MusicBrainzUpdateDatabaseJob.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBMusicBrainzRepository.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBStreamingMusicBrainzImporter.cs`
- `tests/Melodee.Tests.Common/Plugins/SearchEngine/MusicBrainz/MusicBrainzImportBenchmark.cs`

Implementation guide:

1. Use a compiled `Release` harness or a production-equivalent CLI job so build
   time is not counted as import time.
2. Start from a clean target by deleting the target `musicbrainz.ddb` and
   `musicbrainz.ddb-wal`.
3. Monitor the root worker PID and any child processes until import finishes.
4. Capture wall-clock time, total RSS, peak `VmHWM`, total CPU time, final
   `.ddb` size, final `.wal` size, and post-import probe timings.
5. Record the final numbers in this document under "Final Real-File Metrics".

Acceptance target:

- The real-file import finishes without manual interruption.
- The final metrics table is filled in.
- Post-import probes include at least exact name, exact alias, first-row
  existence, and exact MBID lookup.

## DDB-002: Exact MusicBrainzIdRaw Lookup Validation

**Status**: TODO/VERIFY

Exact MBID lookup should be one of the fastest MusicBrainz paths because it is
an equality search on an indexed column. The prior probe showed roughly
`34s`, which is not acceptable for request-path use and needs direct proof on a
rebuilt database.

Relevant code:

- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBMusicBrainzRepository.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/MusicBrainzDbContext.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBStreamingMusicBrainzImporter.cs`

Implementation guide:

1. Use the rebuilt database from DDB-001.
2. Sample known artist MBIDs directly from the materialized `Artist` table.
3. Verify that `MusicBrainzIdRaw` values are canonical strings and round-trip
   through the provider without binary or formatting artifacts.
4. Time direct equality lookup:
   `context.Artists.Where(a => a.MusicBrainzIdRaw == mbid).Take(1)`.
5. Time repository lookup through `SearchArtist(...)` when only an MBID is
   supplied.
6. Time repository lookup when both exact name and MBID are supplied.
7. Compare cold and warm timings to exact normalized-name lookup.

Acceptance target:

- Warm MBID lookup is in the same broad request-safe class as exact
  normalized-name lookup.
- Stored values are readable canonical strings.
- If timing remains slow, DDB-003 receives a reproducible provider/data issue
  with the query, schema, sample value, row count, timings, and generated SQL.

## DDB-003: Indexed String Equality Provider Follow-Up

**Status**: TODO/CONDITIONAL

Only start this work if DDB-002 proves the rebuilt database still has slow
indexed string equality or bad string round-tripping.

Implementation guide:

1. Reproduce the issue in the smallest DecentDB provider test possible.
2. Include a large-row-count case if the issue only appears at scale.
3. Check parameter binding, string storage type, index creation, index
   selection, and data-reader string decoding.
4. Add provider regression tests for equality on indexed string columns.
5. Re-run the DDB-002 probe after the provider fix lands.

Acceptance target:

- Equality lookup on a representative indexed string column is consistently
  request-safe.
- Provider tests fail before the fix and pass after it.

## DDB-004: Local Artist Cache Search Refactor

**Status**: TODO

The local artist cache is smaller than MusicBrainz and has caching in front of
it, but it still contains query shapes that will not age well if the cache grows
to hundreds of thousands or millions of rows.

Current risk areas:

- `ArtistSearchEngineService.DoSearchAsync(...)` performs a wide mixed `OR`
  query across normalized name, MusicBrainz ID, Spotify ID, and tagged
  `AlternateNames.Contains(...)`.
- The second lookup after provider results repeats a similar mixed `OR` query
  across several external IDs and alternate names.
- `Include(x => x.Albums)` is applied before the candidate set is known to be
  small.

Relevant code:

- `src/Melodee.Common/Services/SearchEngines/ArtistSearchEngineService.cs`
- `src/Melodee.Common/Models/SearchEngines/ArtistSearchEngineServiceData`

Implementation guide:

1. Split local cache lookup into ordered phases:
   exact provider IDs, exact `NameNormalized`, exact normalized alias, then any
   intentionally slower fallback.
2. Avoid `AlternateNames.Contains(...)` for the normal fast path. If alternate
   names must be searchable, add or reuse a normalized alias structure.
3. Load albums only after a bounded candidate artist set has been selected.
4. Preserve current caching behavior and provider ordering.
5. Add tests proving an exact local hit does not call external providers and
   that duplicate provider results do not create duplicate artists.

Acceptance target:

- The normal local-cache path no longer relies on one wide mixed `OR` query.
- The normal local-cache path does not use substring alternate-name matching.
- Album loading happens after candidate narrowing.

## DDB-005: Local Cache Listing Scale Hardening

**Status**: TODO

`ArtistSearchEngineService.ListAsync(...)` currently performs a total count and
then materializes the filtered artist query before applying `Skip` and `Take`
in memory. That is acceptable only while the local cache stays small.

Relevant code:

- `src/Melodee.Common/Services/SearchEngines/ArtistSearchEngineService.cs`

Implementation guide:

1. Make total count optional for UI paths that do not strictly require it.
2. Keep pagination on the database side when the provider can translate the
   shape reliably.
3. If provider limitations require a workaround, prefer keyset pagination or a
   two-step ID page query over full table materialization.
4. Keep album counts as a second grouped query over the page's artist IDs only.
5. Add tests for count-only requests, normal page requests, and filtered page
   requests.

Acceptance target:

- A normal page request does not materialize all matching artists before paging.
- Total count can be skipped by callers that do not need it.
- Album counts remain limited to the returned page.

## DDB-006: Import Completion Counts

**Status**: TODO

The import path still uses large-table `CountAsync()` calls for completion
logging and some progress calculations. These are offline operations, so they
are lower risk than request-path queries, but they still add avoidable work on
very large databases.

Relevant code:

- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBMusicBrainzRepository.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBStreamingMusicBrainzImporter.cs`

Implementation guide:

1. Track inserted artist, alias, album, and relation counts during
   materialization.
2. Return an import summary from the streaming importer instead of requiring the
   repository to count materialized tables at the end.
3. Use known staging/materialization counts for progress when they are already
   available.
4. Keep any full-table verification counts behind explicit diagnostic or
   validation settings.

Acceptance target:

- Normal import completion logging does not need final full-table `CountAsync()`
  probes.
- Diagnostic full-table counts remain available when explicitly requested.

## DDB-007: MusicBrainz Query Perf Probe

**Status**: TODO

Add a committed probe so future agents can verify the query shapes that matter
without rebuilding ad hoc scripts each time.

Probe coverage:

- database open and `CanConnectAsync()`
- first-row projection existence check
- exact normalized-name lookup
- exact alias lookup
- exact `MusicBrainzIdRaw` lookup
- any intentionally slow fallback, if one is ever reintroduced

Implementation guide:

1. Put the probe somewhere explicit and non-flaky, such as a benchmark project,
   a manual integration test category, or a documented CLI diagnostic.
2. Require an explicit database path so CI does not need the full MusicBrainz
   file.
3. Emit machine-readable output, preferably JSON, with cold and warm timings.
4. Record sanitized sample query values and row counts.

Acceptance target:

- A developer can run one documented command against a target `.ddb` file and
  get comparable timings for all critical query shapes.
- The probe output is suitable for attaching to future performance issues.

## DDB-008: MusicBrainz Import Perf Probe

**Status**: TODO

Add a committed import perf harness that separates import work from build time
and records the data needed to catch regressions in large MusicBrainz imports.

Probe coverage:

- artist staging time
- artist materialization time
- album staging time
- album materialization time
- relation materialization time
- peak RSS / `VmHWM`
- `.ddb` and `.wal` size growth over time
- final imported row counts from known import counters

Implementation guide:

1. Run from compiled `Release` output.
2. Emit periodic metric samples to JSON or CSV.
3. Support cancellation without corrupting the metric file.
4. Keep the probe manual or opt-in unless a small synthetic mode is added for
   regular CI.

Acceptance target:

- Real-file import runs can be compared across commits without relying on
  shell-only monitoring scripts.
- Phase timing and memory regressions are visible from the probe output.

## DDB-009: Large-Text Search Strategy

**Status**: TODO

The large MusicBrainz request path should not silently rely on substring scans
over blob-like text fields. Decide whether this belongs in DecentDB provider
features, Melodee schema design, or explicit documentation.

Implementation guide:

1. Decide whether DecentDB should support a full-text or token index strategy
   for this class of workload.
2. If provider support is not planned, document that large substring workloads
   require schema-level lookup tables.
3. Keep MusicBrainz alternate-name lookup based on normalized exact-match rows.
4. Do not reintroduce automatic substring fallback on the large request path.

Acceptance target:

- The intended approach is documented in the provider and/or Melodee docs.
- Any future fuzzy search work has an explicit schema and cost model.

## DDB-010: Large-File Diagnostics

**Status**: TODO

Large-file plan problems should be visible before they turn into production
latency surprises.

Implementation guide:

1. Add or expose sanitized SQL logging for DecentDB-backed probes.
2. Include elapsed time, row count, database path or logical connection name,
   and query phase name.
3. Add plan/index diagnostics if DecentDB exposes enough information.
4. Keep diagnostics opt-in so normal production logs do not become noisy.
5. Wire the diagnostics into DDB-007 and DDB-008 where practical.

Acceptance target:

- A slow query report includes enough detail to distinguish a bad query shape
  from provider/index behavior.
- Diagnostics can be enabled without editing source code.

## Final Real-File Metrics

Fill this table after DDB-001 and DDB-002 are run.

| Metric | Value | Notes |
| --- | --- | --- |
| Rebuild command or harness | TBD | Use compiled `Release` output. |
| Source dump path | TBD | Record exact dump/staging path used. |
| Target database path | TBD | Record exact `.ddb` path. |
| Wall-clock time | TBD | Full run only. |
| Peak RSS / `VmHWM` | TBD | Capture root worker and child process behavior. |
| Final `.ddb` size | TBD | Bytes and human-readable size. |
| Final `.wal` size | TBD | Bytes and human-readable size. |
| First-row existence probe | TBD | Cold and warm timings. |
| Exact normalized-name lookup | TBD | Cold and warm timings. |
| Exact alias lookup | TBD | Cold and warm timings. |
| Exact `MusicBrainzIdRaw` lookup | TBD | Cold and warm timings. |

## Current Working Assumption

Melodee does not need to abandon `DecentDB.EntityFrameworkCore` for the large
MusicBrainz file. The remaining risk is narrower: prove the final real-file
numbers, fix or explain exact MBID lookup, and prevent smaller local-cache query
shapes from becoming future scale problems.
