## DecentDB EntityFramework Improvements for Melodee

**Date**: 2026-03-21
**Last updated**: 2026-06-10
**Status**: Melodee work implemented; DecentDB provider follow-up remains after 2.10.0 validation

## Purpose

This document is now an active implementation backlog for the remaining
DecentDB and Melodee scale work. Historical fixes that have already landed were
removed so coding agents can treat the table below as the source of truth for
remaining validation and provider follow-up. The current package baseline is
`DecentDB.AdoNet` `2.10.0`, `DecentDB.EntityFrameworkCore` `2.10.0`, and
`DecentDB.EntityFrameworkCore.NodaTime` `2.10.0`. The real-file measurements
below remain labeled with the DecentDB version that produced them.

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
| PARTIAL | Some implementation or instrumentation exists, but the acceptance target is not satisfied. |
| DONE | Melodee implementation and validation work is complete. |
| UPSTREAM-FIXED | DecentDB upstream has code and regression coverage, but Melodee has not yet validated an available package against the required real-file scenario. |
| PROVIDER-FOLLOWUP | Melodee has a repro or mitigation; remaining work belongs in DecentDB provider or tooling. |

## Active Work Table

| ID | Status | Priority | Owner | Area | Acceptance target |
| --- | --- | --- | --- | --- | --- |
| DDB-001 | DONE | High | Melodee | Real-file MusicBrainz rebuild | Clean monitored rebuild completed against DecentDB `2.8.0`; final wall-clock, memory, `.ddb`, `.wal`, and post-import probe timings are recorded below. |
| DDB-002 | PROVIDER-FOLLOWUP | High | Melodee / DecentDB | Exact `MusicBrainzIdRaw` lookup | DecentDB `2.10.0` proves the ordered indexed equality shape reaches `IndexSeek`, but the checkpointed real-file warm MBID lookup is still about `3.6 s`, so the acceptance target is not met. |
| DDB-003 | PROVIDER-FOLLOWUP | High | DecentDB provider | Indexed string equality | DecentDB `2.10.0` planner/provider fixes are consumed, but checkpointed real-file `Artist` indexed string equality remains multi-second despite captured `IndexSeek` plans. |
| DDB-004 | DONE | Medium | Melodee | Local artist cache search | Local cache search now uses staged exact provider-ID, normalized-name, and normalized-alias phases; normal lookup no longer uses mixed `OR` or substring alias matching. |
| DDB-005 | DONE | Medium | Melodee | Local cache listing | Normal page requests now page in the database and avoid mandatory full count and full materialization; count-only requests still return exact counts. |
| DDB-006 | DONE | Low | Melodee | Import completion counts | Streaming import now returns materialization counts; normal completion logging no longer performs final full-table `CountAsync()` probes. |
| DDB-007 | DONE | Medium | Melodee | MusicBrainz query perf probes | `musicbrainz-query-probe` emits JSON cold/warm timings, SQL shape, DecentDB `EXPLAIN` output, sample values, row counts, and index metadata. |
| DDB-008 | DONE | Medium | Melodee | MusicBrainz import perf probes | `musicbrainz-import-probe` emits JSON phase timings, memory samples, database/WAL growth, CPU time, and importer-reported row counts. |
| DDB-009 | DONE | Medium | DecentDB provider / Melodee docs | Large-text search strategy | Melodee strategy is documented in `design/docs/decentdb-large-text-search-strategy.md`; provider candidates are listed separately. |
| DDB-010 | DONE | Medium | DecentDB provider / Melodee | Large-file diagnostics | Manual probes now expose practical app-level SQL, timing, row-count, index, memory, and file-growth diagnostics. |

## DecentDB 2.10.0 Binding Update

Melodee now consumes the DecentDB `2.10.0` .NET packages. The MusicBrainz
import checkpoint path uses `DecentDBMaintenance.CheckpointAsync(...)`, so
Melodee no longer opens the database manually for this maintenance step and
does not invoke an external DecentDB process anywhere in the repository.

The `2.10.0` binding release covers the Melodee-requested .NET maintenance,
diagnostic, and planner surface:

- file-path maintenance helpers for WAL status, checkpoint, compact, vacuum,
  and index rebuild
- checkpoint result snapshots with before/after WAL sizes
- ADO.NET query-plan diagnostics for provider-level investigation
- clearer ADO.NET open failure diagnostics for unsupported database formats
- provider regression coverage for indexed string equality translation
- native ordered indexed equality projection support for provider-generated
  `ORDER BY`, `LIMIT`, and `OFFSET` query shapes

## DecentDB 2.10.0 Real-File Validation

The DecentDB `2.10.0` package was validated on 2026-06-10 with the committed
Melodee probes against the real MusicBrainz dump. The planner/provider fix is
present: exact normalized-name, exact alias, and exact `MusicBrainzIdRaw`
queries all capture DecentDB `EXPLAIN` output with `IndexSeek`. The runtime
acceptance target is still not met for the large `Artist` table.

Validation artifacts:

- Import probe:
  `.tmp/decentdb-2.10-validation/musicbrainz-import-probe.json`
- WAL-backed query probe:
  `.tmp/decentdb-2.10-validation/musicbrainz-query-probe-wal-backed.json`
- Checkpointed query probe:
  `.tmp/decentdb-2.10-validation/musicbrainz-query-probe-checkpointed.json`

Real-file import and maintenance results:

| Metric | Value | Notes |
| --- | ---: | --- |
| Import duration | `46.72 min` | `2,802.96 s`; build time excluded. |
| Imported artists / aliases / relations / albums | `2,887,383` / `492,790` / `834,044` / `3,705,849` | Counts reported by the streaming importer summary. |
| Peak working set | `21.04 GiB` | From the import probe process `PeakWorkingSetBytes`. |
| Post-import `.ddb` / `.wal` before checkpoint | `8 KB` / `5.1 GB` | Import probe leaves the WAL uncheckpointed. |
| Native checkpoint duration | `557.47 s` | Run through DecentDB `2.10.0` ADO.NET `DecentDBMaintenance.CheckpointAsync(...)`. |
| Post-checkpoint `.ddb` / `.wal` | `2.4 GB` / `32 B` | Checkpoint completed without DecentDB CLI usage. |

Real-file query results:

| Query shape | WAL-backed warm | Checkpointed warm | Plan |
| --- | ---: | ---: | --- |
| `Database.CanConnectAsync()` | `52.86 ms` | `55.59 ms` | Not applicable. |
| Ordered first-row projection | `7,763.99 ms` | `3,372.08 ms` | `TableScan(table=Artist)`. |
| Exact normalized-name lookup | `8,094.76 ms` | `3,495.39 ms` | `IndexSeek(table=Artist, index=IX_Artist_NameNormalized)`. |
| Exact alias lookup | `6,119.85 ms` | `260.08 ms` | `IndexSeek(table=ArtistAlias, index=IX_ArtistAlias_NameNormalized)`. |
| Exact `MusicBrainzIdRaw` lookup | `8,173.73 ms` | `3,626.46 ms` | `IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw)`. |

DDB-002 and DDB-003 therefore remain `PROVIDER-FOLLOWUP`. The remaining
DecentDB issue is no longer EF translation or parameter binding. It is a
provider/runtime/storage issue where `IndexSeek` plans on the checkpointed
large `Artist` table still require multi-second execution. No Melodee CLI
fallback or ad hoc DecentDB process invocation should be added.

## Historical Measurements That Still Drive The Work

The following measurements came from a focused probe against a real
MusicBrainz DecentDB file of roughly `2.31 GB`. They remain useful because they
explain why the open items exist.

| Query shape | Prior timing | Current instruction |
| --- | ---: | --- |
| `Database.CanConnectAsync()` | ~3-13 ms | Safe connectivity probe. |
| Ordered first-row projection, for example `OrderBy(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync()` | ~7-15 ms | DecentDB `2.10.0` checkpointed real-file validation now shows this table-scan shape around `3.4 s`; keep off hot paths until the provider/runtime has a bounded row-id/limit fast path. |
| `Artists.AnyAsync()` / `Artists.Take(1).CountAsync()` | ~13 s | Keep off request paths and avoid in large-table probes. |
| Exact normalized-name MusicBrainz artist lookup | ~13-40 ms | Intended primary large-file search path, but DecentDB `2.10.0` checkpointed validation still leaves DDB-003 open at about `3.5 s` warm. |
| `AlternateNames.Contains(...)` substring lookup | ~15 s | Do not use blob substring scans on the large request path. |
| Tokenized `Contains(...)` fallback | ~22 s | Do not reintroduce as an automatic large-file fallback. |
| Exact `MusicBrainzIdRaw` lookup | ~34 s | DDB-002 and DDB-003 must explain or fix this. |

## DDB-001: Clean Monitored Real-File Rebuild

**Status**: DONE

The MusicBrainz import was run against the real dump to completion and the
final metrics were recorded below. The proof used DecentDB `2.8.0`.

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

**Status**: PROVIDER-FOLLOWUP

Exact MBID lookup should be one of the fastest MusicBrainz paths because it is
an equality search on an indexed column. The prior probe showed roughly
`34s`, which is not acceptable for request-path use. DecentDB `2.10.0` fixes
the ordered indexed equality execution shape that Melodee emits, and the real
probe now captures `IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw)`.
The checkpointed warm lookup still takes about `3.6 s`, so this is not fully
resolved.

Relevant code:

- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBMusicBrainzRepository.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/MusicBrainzDbContext.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBStreamingMusicBrainzImporter.cs`

Validation guide:

1. Use the rebuilt database from DDB-001.
2. Sample known artist MBIDs directly from the materialized `Artist` table.
3. Verify that `MusicBrainzIdRaw` values are canonical strings and round-trip
   through the provider without binary or formatting artifacts.
4. Time direct equality lookup:
   `context.Artists.Where(a => a.MusicBrainzIdRaw == mbid).OrderBy(a => a.Id).Take(1)`.
5. Time repository lookup through `SearchArtist(...)` when only an MBID is
   supplied.
6. Time repository lookup when both exact name and MBID are supplied.
7. Compare cold and warm timings to exact normalized-name lookup.
8. If a future package still shows slow checkpointed/reopened-file timings
   while `EXPLAIN` reports `IndexSeek`, track the issue as DecentDB
   runtime/storage cost for indexed row lookup on large checkpointed tables.

Acceptance target:

- Warm MBID lookup is in the same broad request-safe class as exact
  normalized-name lookup.
- Stored values are readable canonical strings.
- If timing remains slow, DDB-003 receives a reproducible provider/data issue
  with the query, schema, sample value, row count, timings, and generated SQL.

## DDB-003: Indexed String Equality Provider Follow-Up

**Status**: PROVIDER-FOLLOWUP

DDB-002 proved that rebuilt-file indexed string equality was still slow enough
to keep off Melodee request paths under the DecentDB `2.8.0` baseline. Exact
normalized-name and exact `MusicBrainzIdRaw` lookups both remained around
`7.6 s` warm against the clean rebuild, with exact alias lookup around `5.7 s`
warm.

DecentDB `2.10.0` includes the concrete provider/planner follow-up from this
document:

- simple indexed equality projections accept simple ordering, limits, and
  offsets instead of falling through to slower projection paths
- the native executor has a regression that fails if ordered indexed equality
  does not stay on the fast path
- the EF Core provider has a large indexed string regression for
  `Where(...).OrderBy(...).Take(1)`
- ADO.NET has binding-native index rebuild helpers for maintenance workflows

The `2.10.0` real-file validation proves those planner fixes are present, but
the runtime target is still not satisfied for the checkpointed large `Artist`
table. Warm `Artist.NameNormalized == value` takes about `3.5 s`, and warm
`Artist.MusicBrainzIdRaw == value` takes about `3.6 s`, even though both plans
show `IndexSeek`.

Provider enhancement candidates are tracked in
`design/docs/decentdb-provider-enhancements.md`.

Release validation guide:

1. Keep the `2.10.0` query probe artifacts attached to any follow-up issue.
2. Reproduce the remaining issue below EF Core, using the captured SQL and
   `EXPLAIN` plans.
3. Check indexed row lookup/materialization cost for checkpointed large tables,
   not only planner index selection.
4. Re-run `musicbrainz-query-probe` after the next provider/runtime fix.

Acceptance target:

- Equality lookup on a representative indexed string column is consistently
  request-safe.
- Provider tests fail before the fix and pass after it.

## DecentDB Provider Enhancement Candidates

The current Melodee work exposed several DecentDB provider or .NET binding
enhancement candidates:

- native .NET checkpoint, vacuum, compact, and WAL-status APIs published in
  DecentDB `2.9.0`
- clearer WAL checkpoint status published in DecentDB `2.9.0`; documented
  `WalAutoCheckpoint` guidance remains useful provider documentation
- regression coverage for equality on indexed string columns published in
  DecentDB `2.9.0`; ordered indexed equality fast-path coverage and binding
  index rebuild helpers published in DecentDB `2.10.0`
- opt-in query plan and index usage diagnostics through ADO.NET, published in
  DecentDB `2.9.0`
- real-file DecentDB `2.10.0` validation shows `IndexSeek` plans but
  multi-second `Artist` table indexed equality execution, so runtime/storage
  follow-up remains
- documented large-text or full-text search strategy
- clearer unsupported file-format diagnostics published in DecentDB `2.9.0`;
  explicit upgrade guidance remains a provider documentation candidate

See `design/docs/decentdb-provider-enhancements.md` for the detailed list and
the exact observations behind each item.

## DDB-004: Local Artist Cache Search Refactor

**Status**: DONE

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

**Status**: DONE

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

**Status**: DONE

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

**Status**: DONE

Add a committed probe so future agents can verify the query shapes that matter
without rebuilding ad hoc scripts each time.

Probe coverage:

- database open and `CanConnectAsync()`
- ordered first-row projection existence check
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

**Status**: DONE

Extend the committed import perf harnesses so they separate import work from
build time and record the data needed to catch regressions in large MusicBrainz
imports.

Current code state:

- `benchmarks/Melodee.Benchmarks/MusicBrainzImportBenchmarks.cs` provides
  BenchmarkDotNet coverage for synthetic MusicBrainz import data.
- `tests/Melodee.Tests.Common/Plugins/SearchEngine/MusicBrainz/MusicBrainzImportBenchmark.cs`
  provides opt-in synthetic perf tests behind `MELODEE_RUN_PERF_TESTS=true`.
- `benchmarks/Melodee.Benchmarks/MusicBrainzImportProbe.cs` provides the
  manual real-file import mode, OS-level RSS and `VmHWM` samples on Linux,
  periodic `.ddb` and `.wal` growth samples, phase timings, and final counts
  from importer counters.

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
4. Keep the real-file probe manual or opt-in; retain synthetic coverage for
   regular CI or local smoke checks.

Acceptance target:

- Real-file import runs can be compared across commits without relying on
  shell-only monitoring scripts.
- Phase timing and memory regressions are visible from the probe output.

## DDB-009: Large-Text Search Strategy

**Status**: DONE

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

**Status**: DONE

Large-file plan problems should be visible before they turn into production
latency surprises.

Current code state:

- `DecentDBMusicBrainzRepository` logs phase timing for MusicBrainz artist
  searches.
- `MusicBrainzUpdateDatabaseJob` logs WAL size before and after the DecentDB
  checkpoint step through `DecentDBMaintenance.CheckpointAsync(...)`.
- The query and import probes expose opt-in sanitized SQL, row counts, index
  metadata, query-plan output from DecentDB's ADO.NET `ExplainQuery` helper,
  memory samples, file growth, and phase timings.

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

## Historical DecentDB 2.8.0 Real-File Metrics

This table records the completed DDB-001 and DDB-002 run against DecentDB `2.8.0`.

| Metric | Value | Notes |
| --- | --- | --- |
| Rebuild command or harness | `dotnet benchmarks/Melodee.Benchmarks/bin/Release/net10.0/Melodee.Benchmarks.dll musicbrainz-import-probe --storage /mnt/fileserver_db_storage/melodee/search-engine-storage/musicbrainz --db /tmp/melodee-decentdb-probes/musicbrainz.ddb --output /tmp/melodee-decentdb-probes/musicbrainz-import-probe.json --clean --sample-interval-ms 10000` | Compiled `Release` output. |
| Source dump path | `/mnt/fileserver_db_storage/melodee/search-engine-storage/musicbrainz/staging/mbdump` | Existing extracted real MusicBrainz dump. |
| Target database path | `/tmp/melodee-decentdb-probes/musicbrainz.ddb` | Clean target. Existing files were deleted first. |
| Wall-clock time | `45.49 min` | `2,729.35 s`; build time excluded. |
| Imported artists / aliases / relations / albums | `2,887,383` / `492,790` / `834,044` / `3,705,849` | Counts reported by the streaming importer summary, not final `CountAsync()` probes. |
| Peak RSS / `VmHWM` | `20.68 GiB` peak process working set; manual `/proc` check observed `VmHWM` around `21 GiB` | Probe report also contains periodic memory samples. |
| Final `.ddb` size | `8 KB` | Main file stayed small because checkpointing was unavailable. |
| Final `.wal` size | `5.1 GB` | This run happened before DecentDB `2.9.0` binding maintenance helpers were consumed; Melodee now checkpoints imported databases through `DecentDBMaintenance.CheckpointAsync(...)`. |
| First-row existence probe | cold `7,419 ms`; warm `7,509 ms` | Final query probe: `/tmp/melodee-decentdb-probes/musicbrainz-query-probe-final.json`. |
| Exact normalized-name lookup | cold `7,748 ms`; warm `7,620 ms` | Indexed `Artist.NameNormalized` equality. |
| Exact alias lookup | cold `5,659 ms`; warm `5,665 ms` | Indexed `ArtistAlias.NameNormalized` equality. |
| Exact `MusicBrainzIdRaw` lookup | cold `7,718 ms`; warm `7,597 ms` | Indexed `Artist.MusicBrainzIdRaw` equality. |

## Current Working Assumption

Melodee does not need to abandon `DecentDB.EntityFrameworkCore` for the large
MusicBrainz file. The Melodee-side query-shape, import-count, local-cache, and
diagnostic work is implemented. DecentDB `2.10.0` provides the binding-native
checkpoint, WAL-status, query-plan, compatibility diagnostic, ordered indexed
equality, and index rebuild helpers requested by Melodee, and Melodee consumes
the maintenance helper for MusicBrainz imports. The remaining gap is now proven
below the EF translation layer: checkpointed real-file `Artist` indexed
equality uses `IndexSeek` but still executes in multiple seconds. DDB-002 and
DDB-003 should stay open until a DecentDB provider/runtime/storage fix makes
those equality lookups request-safe on the real MusicBrainz file.
