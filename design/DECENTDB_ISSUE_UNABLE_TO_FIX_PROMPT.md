## DecentDB Indexed Equality Performance Issue Handoff Prompt

**Status**: Historical handoff prompt. The DecentDB local worktree fix was
validated on 2026-06-15 by wiring Melodee directly to
`/home/steven/src/github/decentdb/bindings/dotnet`, and DecentDB `2.13.1`
NuGet package validation reproduced the fix. See
`design/DECENTDB_IMPROVEMENTS.md` for the current `DONE` status.

Use this prompt for a DecentDB coding agent working in the local DecentDB
repository at `/home/steven/src/github/decentdb`.

## Situation

Melodee has tried to resolve DDB-002 and DDB-003 across multiple DecentDB
package releases. The current Melodee package baseline is:

- `DecentDB.AdoNet` `2.13.0`
- `DecentDB.EntityFrameworkCore` `2.13.0`
- `DecentDB.EntityFrameworkCore.NodaTime` `2.13.0`

The `2.13.0` package still does not satisfy Melodee's real-file acceptance
target for DDB-002 or DDB-003.

The important distinction is that DecentDB now chooses the expected query
plans, but the real execution remains far too slow:

- `Artist.NameNormalized == value` reports `IndexSeek`.
- `Artist.MusicBrainzIdRaw == value` reports `IndexSeek`.
- Checkpointed execution against the real MusicBrainz-sized `Artist` table
  still does not complete in a request-safe window; targeted `2.13.0` probes
  timed out after `180 s`.

This means the remaining problem is likely below EF Core translation and below
simple planner index selection. The issue appears to be in DecentDB runtime,
storage, index lookup, row materialization, sort/limit execution after an
index seek, checkpointed-file access, or a closely related path.

Do not mark this issue fixed until the DecentDB repository has tests that fail
before the fix and pass after it, and Melodee's real-file probe proves the
checkpointed query timings are request-safe.

## Why This Matters

Melodee uses DecentDB for a large, read-mostly MusicBrainz materialized
database. This database is used by ingestion and artist lookup workflows.
MusicBrainz artist data is large enough that full scans or inefficient
post-index work become user-visible delays.

DDB-002 is the exact `MusicBrainzIdRaw` lookup path. This should be one of the
fastest possible lookups because it is an equality search over an indexed
canonical identifier column.

DDB-003 is the more general indexed string equality path. Melodee depends on
indexed normalized strings such as `NameNormalized` and normalized alias lookup
rows to avoid substring scans over large text columns.

If exact indexed equality on a checkpointed multi-million-row table takes
multiple seconds, Melodee cannot safely put these lookups on request paths.
The application can avoid bad query shapes, but it cannot compensate for a
multi-second indexed equality execution path inside the database engine.

## Current Melodee Evidence

The latest Melodee validation used DecentDB `2.13.0` on 2026-06-15.

The existing production MusicBrainz database could not be reused because it was
an older unsupported DecentDB file format. Validation therefore used a fresh
real-file rebuild from the MusicBrainz dump.

Artifacts from the Melodee validation run:

- Import probe:
  `.tmp/decentdb-2.13-validation/musicbrainz-import-probe.json`
- ADO.NET plan capture:
  `.tmp/decentdb-2.13-validation/musicbrainz-explain-plans.txt`
- Checkpointed targeted query probes were attempted, but timed out before a
  JSON report was written.

Import and checkpoint results:

| Metric | Value |
| --- | ---: |
| Import duration in minutes | `46.62 min` |
| Import duration in seconds | `2,797.22 s` |
| Artists | `2,887,383` |
| Artist aliases | `492,790` |
| Artist relations | `834,044` |
| Albums | `3,705,849` |
| Peak working set | `20.33 GiB` |
| Post-import main `.ddb` before checkpoint | `8 KB` |
| Post-import `.wal` before checkpoint | `5.0 GiB` |
| Native checkpoint duration | `635.93 s` |
| Post-checkpoint main `.ddb` | `2.4 GiB` |
| Post-checkpoint `.wal` | `32 B` |

The checkpoint was run through the DecentDB .NET binding:

```csharp
await DecentDBMaintenance.CheckpointAsync(databasePath, cancellationToken);
```

No DecentDB CLI was used for the Melodee validation.

## Current Failing Validation

DecentDB `2.13.0` still reports the expected index plans:

| Query shape | Plan capture | Plan |
| --- | ---: | --- |
| Exact normalized-name lookup | `17.28 ms` | `IndexSeek(table=Artist, index=IX_Artist_NameNormalized)` |
| Exact alias lookup | `0.27 ms` | `IndexSeek(table=ArtistAlias, index=IX_ArtistAlias_NameNormalized)` |
| Exact `MusicBrainzIdRaw` lookup | `0.16 ms` | `IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw)` |

The actual checkpointed execution still fails:

| Targeted probe | Result |
| --- | --- |
| Explicit `MusicBrainzIdRaw` sample, with name and alias supplied | Timed out after `180 s`. |
| Explicit normalized-name sample, with alias supplied | Timed out after `180 s`. |

The checkpointed values are the relevant acceptance case for Melodee because
the MusicBrainz update job checkpoints the imported database before promoting
it.

These timeouts are not acceptable. An indexed equality lookup on one row, or a
very small candidate set, should not exceed a `180 s` validation timeout after
the file is checkpointed and reopened.

## Important Sample Values

The `2.13.0` query probe used this sample:

```text
FirstArtistId: 1
NameNormalized: 0JTQvtC60YLQvtGAINCh0LDRgtCw0L3QsA
AliasNormalized: 0KDQsNC30L3QuCDQmNC30LLQtdC00YPQstCw0YfQuA
MusicBrainzIdRaw: fadeb38c-833f-40bc-9d8c-a6383b38b1be
```

The exact `MusicBrainzIdRaw` value should be highly selective. If the engine
is doing significant work after the index seek, that work needs to be
explained and reduced.

## Melodee Query Shapes

Melodee's benchmark probe measures these EF Core query shapes.

Ordered first-row existence:

```csharp
context.Artists
    .AsNoTracking()
    .OrderBy(a => a.Id)
    .Select(a => a.Id)
    .Take(1)
```

Exact normalized artist name:

```csharp
context.Artists
    .AsNoTracking()
    .Where(a => a.NameNormalized == sampleValues.NameNormalized)
    .OrderBy(a => a.SortName)
    .Take(10)
```

Exact normalized alias:

```csharp
context.ArtistAliases
    .AsNoTracking()
    .Where(a => a.NameNormalized == sampleValues.AliasNormalized)
    .OrderBy(a => a.MusicBrainzArtistId)
    .Take(10)
```

Exact MusicBrainz ID:

```csharp
context.Artists
    .AsNoTracking()
    .Where(a => a.MusicBrainzIdRaw == sampleValues.MusicBrainzIdRaw)
    .OrderBy(a => a.Id)
    .Take(1)
```

The probe also sends equivalent SQL to DecentDB's ADO.NET `ExplainQuery`
helper so the report includes plan diagnostics.

Exact normalized artist name SQL shape:

```sql
SELECT *
FROM "Artist"
WHERE "NameNormalized" = '0JTQvtC60YLQvtGAINCh0LDRgtCw0L3QsA'
ORDER BY "SortName"
LIMIT 10
```

Exact MusicBrainz ID SQL shape:

```sql
SELECT *
FROM "Artist"
WHERE "MusicBrainzIdRaw" = 'fadeb38c-833f-40bc-9d8c-a6383b38b1be'
ORDER BY "Id"
LIMIT 1
```

## Captured Plans

Checkpointed exact normalized-name plan:

```text
Limit(limit=10, offset=none)
  Sort(SortName)
    Project(*)
      IndexSeek(table=Artist, index=IX_Artist_NameNormalized, predicate=(NameNormalized = '0JTQvtC60YLQvtGAINCh0LDRgtCw0L3QsA'))
```

Checkpointed exact MusicBrainz ID plan:

```text
Limit(limit=1, offset=none)
  Sort(Id)
    Project(*)
      IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw, predicate=(MusicBrainzIdRaw = 'fadeb38c-833f-40bc-9d8c-a6383b38b1be'))
```

The `EXPLAIN` helper itself is fast. The expensive part is query execution.
For example, in the checkpointed `2.13.0` probe, the plan capture time was
about `0.16 ms` for the exact `MusicBrainzIdRaw` plan, but the actual targeted
query probe timed out after `180 s`.

## Historical Attempts And Why They Were Not Enough

Melodee previously validated DecentDB `2.10.0` and `2.11.0`.

DecentDB `2.10.0` proved the planner/provider could select `IndexSeek`, but
runtime execution was still slow on the real checkpointed `Artist` table:

| Query shape | Checkpointed warm timing | Plan |
| --- | ---: | --- |
| Ordered first-row projection | `3,372.08 ms` | `TableScan(table=Artist)` |
| Exact normalized-name lookup | `3,495.39 ms` | `IndexSeek(table=Artist, index=IX_Artist_NameNormalized)` |
| Exact alias lookup | `260.08 ms` | `IndexSeek(table=ArtistAlias, index=IX_ArtistAlias_NameNormalized)` |
| Exact `MusicBrainzIdRaw` lookup | `3,626.46 ms` | `IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw)` |

DecentDB `2.11.0` still did not meet the acceptance target:

| Query shape | Checkpointed warm timing | Plan |
| --- | ---: | --- |
| Ordered first-row projection | `3,199.05 ms` | `TableScan(table=Artist)` |
| Exact normalized-name lookup | `3,373.36 ms` | `IndexSeek(table=Artist, index=IX_Artist_NameNormalized)` |
| Exact alias lookup | `256.62 ms` | `IndexSeek(table=ArtistAlias, index=IX_ArtistAlias_NameNormalized)` |
| Exact `MusicBrainzIdRaw` lookup | `3,417.25 ms` | `IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw)` |

DecentDB `2.12.0` still does not meet the target and appears worse for the two
large `Artist` equality paths:

| Query shape | Checkpointed warm timing | Plan |
| --- | ---: | --- |
| Ordered first-row projection | `2,918.39 ms` | `OrderedRowIdScan(table=Artist, column=Id)` |
| Exact normalized-name lookup | `10,293.83 ms` | `IndexSeek(table=Artist, index=IX_Artist_NameNormalized)` |
| Exact alias lookup | `855.80 ms` | `IndexSeek(table=ArtistAlias, index=IX_ArtistAlias_NameNormalized)` |
| Exact `MusicBrainzIdRaw` lookup | `9,398.26 ms` | `IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw)` |

DecentDB `2.13.0` still does not meet the target:

| Query shape | Checkpointed execution result | Plan |
| --- | --- | --- |
| Exact normalized-name lookup | Timed out after `180 s` | `IndexSeek(table=Artist, index=IX_Artist_NameNormalized)` |
| Exact `MusicBrainzIdRaw` lookup | Timed out after `180 s` | `IndexSeek(table=Artist, index=IX_Artist_MusicBrainzIdRaw)` |

The previous fixes were therefore insufficient. Selecting `IndexSeek` is
necessary, but it is not enough.

## Constraints

Do not solve this in Melodee by shelling out to the DecentDB CLI.

Melodee must use native .NET bindings for DecentDB access and maintenance:

- `DecentDB.AdoNet`
- `DecentDB.EntityFrameworkCore`
- `DecentDB.EntityFrameworkCore.NodaTime`
- `DecentDBMaintenance`

Do not claim the issue is fixed only because `EXPLAIN` reports `IndexSeek`.
That has already been proven and is still too slow.

Do not accept a DecentDB change that causes broad performance regressions.
A previous DecentDB attempt degraded benchmark performance by orders of
magnitude. Any proposed fix must include targeted regression tests and must
pass the DecentDB quality and benchmark checks listed below.

## Required DecentDB Work

Work in `/home/steven/src/github/decentdb`.

Investigate why checkpointed large-table indexed equality remains
multi-second even when `EXPLAIN` reports `IndexSeek`.

Create DecentDB-side tests that reproduce the behavior without requiring the
full Melodee repository. The tests should cover at least these cases:

1. A large table with an indexed string column and millions of rows or a
   scaled-down fixture that preserves the same execution bug.
2. A highly selective exact string equality lookup that returns one row.
3. A lookup with `ORDER BY` and `LIMIT`.
4. A lookup without `ORDER BY` to isolate sort cost from index and row fetch
   cost.
5. A checkpoint and reopen cycle before timing the query.
6. Verification that the selected plan is `IndexSeek`.
7. Verification that execution time is request-safe and does not regress.

If a full millions-of-rows unit test is too expensive for normal CI, create:

- a small deterministic regression test that fails before the fix; and
- a larger ignored/manual performance test or benchmark that can be run before
  release.

The large/manual test is still required before publishing a package that
Melodee will treat as a candidate fix.

## Investigation Areas

Start with these hypotheses. They may be wrong, but the final answer must
explain which were ruled out and why.

- `IndexSeek` may find matching keys quickly but perform expensive row
  materialization for each candidate.
- `IndexSeek` may scan a much larger portion of the index than the predicate
  implies.
- Equality on string keys may be doing extra decode, collation, comparison,
  allocation, or normalization work per row.
- The `Sort(...)` above `IndexSeek` may force materialization of many rows
  before `LIMIT`, even when the equality predicate is highly selective.
- `Project(*)` may fetch large row payloads inefficiently from checkpointed
  files.
- Row lookup by row id from an index entry may be slow after checkpoint or
  reopen.
- WAL-backed and checkpointed files may use different access paths or caching
  behavior.
- The `Artist` table may expose a different problem than `ArtistAlias` because
  of row count, row width, index layout, page locality, or payload size.
- The `.coord` or checkpoint metadata path may add overhead when resolving
  index entries to rows.
- The query executor may not stop early enough for `LIMIT 1` after an
  equality predicate.

The final DecentDB fix should identify the actual root cause, not only adjust
one Melodee-specific query.

## Reproduction From Melodee

Use this only as the final integration proof. The DecentDB repository should
also get its own focused tests.

From `/home/steven/src/github/melodee`, with DecentDB packages updated to the
candidate package:

```bash
dotnet restore Melodee.sln
dotnet build Melodee.sln --no-restore
```

Run a clean real-file import probe:

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks --no-restore -- \
  musicbrainz-import-probe \
  --storage /mnt/fileserver_db_storage/melodee/search-engine-storage/musicbrainz \
  --db .tmp/decentdb-validation/musicbrainz.ddb \
  --output .tmp/decentdb-validation/musicbrainz-import-probe.json \
  --clean \
  --sample-interval-ms 10000
```

Run a query probe before checkpoint:

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks --no-restore -- \
  musicbrainz-query-probe \
  --db .tmp/decentdb-validation/musicbrainz.ddb \
  --output .tmp/decentdb-validation/musicbrainz-query-probe-wal-backed.json
```

Checkpoint through the native .NET binding. Do not use the DecentDB CLI for
Melodee validation:

```csharp
await DecentDBMaintenance.CheckpointAsync(databasePath, cancellationToken);
```

Run the checkpointed query probe:

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks --no-restore -- \
  musicbrainz-query-probe \
  --db .tmp/decentdb-validation/musicbrainz.ddb \
  --output .tmp/decentdb-validation/musicbrainz-query-probe-checkpointed.json
```

Extract timings:

```bash
jq -r '.Measurements[] |
  [.Pass, .Name, (.ElapsedMilliseconds | tostring), (.RowCount | tostring),
   (.PlanDiagnostics.Lines | join(" | "))] | @tsv' \
  .tmp/decentdb-validation/musicbrainz-query-probe-checkpointed.json
```

## DecentDB Quality Gate

Before claiming the DecentDB issue is fixed, run DecentDB's normal quality
checks from `/home/steven/src/github/decentdb`:

```bash
python ./scripts/do-pre-commit-checks.py
```

Also run the relevant DecentDB test suites and targeted benchmarks for the
changed runtime/index/storage path.

Because an earlier DecentDB attempt caused severe performance regressions,
also run the DecentDB benchmark coverage that can catch broad execution
slowdowns, including the Rust baseline benchmarks when applicable:

```bash
cd /home/steven/src/github/decentdb/benchmarks/rust-baseline
```

Use the repository's benchmark instructions from there. Record the before and
after results or explain why a benchmark could not be run.

## Acceptance Criteria

The DecentDB work is not complete until all of these are true:

- DecentDB has a focused regression test that fails before the fix and passes
  after the fix.
- DecentDB has coverage for checkpoint and reopen behavior.
- DecentDB proves `IndexSeek` is still selected for the relevant queries.
- DecentDB proves the actual execution time is request-safe, not merely that
  plan generation is fast.
- DecentDB quality checks pass, including
  `python ./scripts/do-pre-commit-checks.py`.
- DecentDB benchmarks do not show broad performance regression.
- Melodee consumes the candidate DecentDB package through NuGet.
- Melodee restore and build pass.
- Melodee's fresh real-file import, native checkpoint, and checkpointed query
  probe show DDB-002 and DDB-003 are request-safe.
- `design/DECENTDB_IMPROVEMENTS.md` is updated with the new package version,
  exact timings, plan output, and the final status.
- `docs/pages/changelog.md` is updated after Melodee validation.

## What Not To Do

Do not close this as fixed based only on a small in-memory or uncheckpointed
database test.

Do not close this as fixed based only on `EXPLAIN`.

Do not close this as fixed based only on planner changes.

Do not add a Melodee workaround that shells out to the DecentDB CLI.

Do not add an unbounded fallback query in Melodee.

Do not trade correctness or broad DecentDB performance for this one query
shape.

## Desired Final Report

When the DecentDB agent finishes, provide a report with:

- root cause;
- files changed;
- tests added;
- benchmarks run;
- before and after DecentDB timings;
- before and after Melodee real-file timings;
- any remaining risks;
- exact DecentDB NuGet package version that Melodee should consume.

If the root cause cannot be fixed in one pass, document the blocker precisely
and keep DDB-002 and DDB-003 open.
