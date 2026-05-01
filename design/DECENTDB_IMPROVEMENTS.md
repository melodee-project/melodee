# DecentDB EntityFramework Improvements for Melodee

**Date**: 2026-03-21
**Status**: Targeted Melodee and DecentDB fixes delivered; synthetic validation complete;
final clean real-file metrics still outstanding

## Summary

Melodee is using `DecentDB.EntityFrameworkCore` for two very different workloads:

- `MusicBrainzConnection` backs the large, read-mostly MusicBrainz materialized database.
- `ArtistSearchEngineConnection` backs the much smaller local artist search cache used by
  Melodee's search-engine enrichment workflow.

Those two workloads should not be treated as if they have the same performance envelope.

The large MusicBrainz `.ddb` is viable for Melodee today, but only if Melodee stays on
index-friendly query shapes and avoids scan-heavy existence or substring patterns on the
request path. The evidence from the real 2.31 GB file is very clear:

- opening the database is fast
- `CanConnectAsync()` is fast
- first-row projection is fast
- exact normalized-name lookup is fast
- `AnyAsync()` and `CountAsync()` existence probes are pathologically slow
- substring searches over `AlternateNames` and tokenized `Contains(...)` fallbacks are slow
- exact `MusicBrainzIdRaw` lookup is unexpectedly slow and needs explicit investigation

For the local artist search cache database, the current Melodee usage is acceptable today
because that database is expected to stay much smaller, and the code has multiple caching
layers in front of it. Even so, a few query/index choices will become pain points if that
local cache is allowed to grow substantially.

The short version is:

- Melodee's doctor path is now using the correct large-file pattern:
  `CanConnectAsync()` plus projected first-row fetches instead of `AnyAsync()` or `CountAsync()`.
- Melodee now materializes exact-match MusicBrainz aliases into a dedicated normalized lookup
  structure instead of relying on substring scans over the `AlternateNames` blob.
- Melodee removed the tokenized `Contains(...)` fallback from the default large-file request path
  and prefers the fast exact-name path before touching the slower exact-MBID path when both values
  are supplied.
- The first real rebuild run proved the large MusicBrainz `.ddb` file was not the startup problem.
  The real problem was Melodee's own import materialization strategy.
- The first import fix addressed memory pressure by keeping album writes batch-bounded and by
  collapsing duplicate valid `ReleaseCountryStaging` rows so one logical release does not fan out
  into duplicate albums.
- Phase-level profiling then showed that the next bottleneck was not album inserts at all. It was
  the album materialization query shape itself.
- Melodee now precomputes helper staging tables for resolved release-country rows and primary
  artist-credit rows so the hot album query joins simple keyed tables instead of repeatedly paying
  for the old aggregate/derived-table shape.
- Melodee now writes large staging/materialization batches through prepared ADO commands where that
  shape is materially better than EF `AddRange(...)` plus `SaveChanges()`.
- The DecentDB .NET bindings were also corrected at the source:
  - local-source builds now load the correct native library instead of stale packaged native
    assets
  - EF `DateTime` / `DateTimeOffset` mapping is back on true DecentDB `TIMESTAMP`
  - `DecentDBDataReader.GetDateTime()` now decodes DecentDB timestamp microseconds correctly
  - UUID-backed values can be read as canonical strings more safely
  - repeated single-statement `ExecuteNonQuery()` calls now reuse prepared statements
- The current synthetic importer validation is green again:
  `StreamingMusicBrainzImporterTests` passed `11/11` in `26s`.
- The main outstanding work is no longer "find the bottleneck." The remaining work is to finish a
  clean monitored real-file rerun and capture the final wall-clock, RSS, file-growth, and
  post-import probe numbers.
- `MusicBrainzIdRaw` exact lookup still deserves explicit validation on the rebuilt database before
  it is trusted as a first-class fast path.
- The local artist cache is in a better place than MusicBrainz, but its wide OR search and admin
  listing patterns are still the most obvious future scale concerns.

## Completed implementation and validation in this session

### Melodee-side changes delivered

The following Melodee changes are no longer theoretical recommendations. They were implemented and
validated during this session:

1. **Doctor/request-path fixes**
   - `DoctorService` now uses `Database.CanConnectAsync()` and projected first-row fetches instead
     of expensive large-table existence patterns.
   - Regression tests were added so the large-file doctor path does not silently regress back to
     `AnyAsync()`/`CountAsync()` behavior.

2. **MusicBrainz lookup hardening**
   - Exact alias lookup moved onto a dedicated normalized alias structure rather than a substring
     scan over `AlternateNames`.
   - The tokenized `Contains(...)` fallback was removed from the normal large-file request path.
   - The repository now prefers exact-name success before paying the suspicious MBID path when both
     values are provided.

3. **MusicBrainz import pipeline fixes**
   - Album materialization was changed from a giant in-memory accumulation model to a batch-bounded
     pipeline.
   - Duplicate valid release-country rows are collapsed before album materialization so one logical
     release does not create duplicate albums.
   - Artist alternate-name materialization was moved off the per-row update loop and into bulk SQL
     aggregation.
   - Album insert persistence moved onto raw prepared ADO inserts with lightweight row buffering
     rather than EF tracking-heavy bulk adds.
   - The album materialization query now uses precomputed helper tables:
     - `ReleaseCountryResolvedStaging`
     - `ArtistCreditPrimaryArtistStaging`

4. **Local cache maintenance**
   - Melodee now ensures the local artist-cache housekeeping index even on already-created DecentDB
     files, rather than assuming a fresh database creation path is the only place the index can be
     established.

### DecentDB provider / binding changes delivered

The following DecentDB .NET changes were implemented in the DecentDB repository and then wired into
Melodee through local project references:

1. **Native asset selection for local/source builds**
   - The .NET build/runtime asset flow was corrected so local-source validation does not quietly
     load stale packaged native binaries.

2. **TIMESTAMP correctness**
   - EF mapping was restored to DecentDB `TIMESTAMP` instead of the temporary integer workaround.
   - `GetDateTime()` decoding was corrected to use microseconds since Unix epoch UTC, matching the
     DecentDB engine's native representation.

3. **Query/execution improvements**
   - `Any()` / existence translation was optimized in the provider.
   - repeated single-statement `ExecuteNonQuery()` now benefits from prepared statement reuse
   - `Prepare()` now actually primes that reusable prepared statement path instead of being a no-op

4. **Data-reader compatibility**
   - UUID-backed values read as strings now come back as canonical GUID strings, which is safer for
     mixed-schema or legacy-read scenarios.

### Validation completed

The fixes above were not left at the "looks right" stage. Validation completed includes:

- full DecentDB .NET solution tests passing after the provider changes (`526/526`)
- targeted ADO validation passing for the prepared statement reuse work (`43/43`)
- direct ADO coverage added for prepared statement reuse
- EF type-mapping and timestamp regression coverage added/updated
- Melodee importer regression coverage for duplicate release-country rows
- latest `StreamingMusicBrainzImporterTests` run: `11/11` passing in `26s`

### Outstanding work

The remaining work is much narrower now:

1. **Finish the clean monitored real-file rerun**
   - the rerun was started from a compiled `Release` probe harness and then intentionally stopped
     at user request to wrap the session
   - final full-run metrics are still needed

2. **Capture final full-file metrics**
   - total wall time
   - peak RSS / `VmHWM`
   - final `.ddb` and `.wal` sizes
   - post-import probe timings

3. **Validate exact `MusicBrainzIdRaw` lookup on the rebuilt database**
   - this remains the most suspicious exact-match path based on the earlier probe

4. **Separate environment issues**
   - the true CLI job path is still blocked in this shell by PostgreSQL-backed configuration access
   - the `Melodee.Tests.Blazor` local-reference test-host crash remains a separate issue from the
     DecentDB work itself

## Scope of this analysis

This analysis focused on the Melodee surfaces that currently use `DecentDB.EntityFrameworkCore`
and that are relevant to real-world user workflows:

- `src/Melodee.Blazor/Program.cs`
- `src/Melodee.Cli/Command/CommandBase.cs`
- `src/Melodee.Blazor/Services/DoctorService.cs`
- `src/Melodee.Cli/Command/DoctorCommand.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/MusicBrainzDbContext.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/Data/DecentDBMusicBrainzRepository.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/MusicBrainzArtistSearchEnginePlugin.cs`
- `src/Melodee.Common/Plugins/SearchEngine/MusicBrainz/MusicBrainzCoverArtArchiveSearchEngine.cs`
- `src/Melodee.Common/Services/SearchEngines/ArtistSearchEngineService.cs`
- `src/Melodee.Common/Models/SearchEngines/ArtistSearchEngineServiceData/*.cs`
- `src/Melodee.Common/Jobs/ArtistSearchEngineRepositoryHousekeepingJob.cs`
- `src/Melodee.Common/Jobs/MusicBrainzUpdateDatabaseJob.cs`

It also used a temporary probe against the real MusicBrainz DecentDB file:

- `/mnt/incoming/melodee_test/search-engine-storage/musicbrainz/musicbrainz.ddb`

## Where Melodee uses DecentDB.EntityFramework today

### DecentDB database registrations

Melodee registers two DecentDB-backed EF Core contexts in both the Blazor app and the CLI:

- `ArtistSearchEngineServiceDbContext` via `ArtistSearchEngineConnection`
- `MusicBrainzDbContext` via `MusicBrainzConnection`

That wiring exists in:

- `src/Melodee.Blazor/Program.cs`
- `src/Melodee.Cli/Command/CommandBase.cs`

This is the correct high-level split. The important point is that these are not just two
copies of the same use case:

- the MusicBrainz database is a large, read-mostly lookup store
- the artist search cache database is a smaller, mutable working set

The provider and Melodee code should optimize differently for each one.

## Workload 1: MusicBrainz lookup database

The MusicBrainz DecentDB file is used by the following Melodee paths:

- Blazor doctor checks
- CLI doctor checks
- artist lookup through `MusicBrainzArtistSearchEnginePlugin`
- cover-art lookup through `MusicBrainzCoverArtArchiveSearchEngine`
- MusicBrainz update/import job
- optional global search when `SearchInclude.MusicBrainz` is enabled

The main repository is `DecentDBMusicBrainzRepository`.

Its search behavior matters the most, because that is where user-facing latency appears.

### Current MusicBrainz query strategy

`DecentDBMusicBrainzRepository.SearchArtist(...)` does a bounded search and caps the
working result size to `10` items internally. That is a good decision for a large
file-backed store.

The search path is currently centered on exact-match phases:

1. exact `NameNormalized` match
2. exact reversed-name match
3. exact alias lookup through the normalized alias structure
4. exact `MusicBrainzIdRaw` match when an MBID is provided and the earlier exact phases did not
   already return results
5. album fan-out query by the found `MusicBrainzArtistId` values

This is a much better mix of shapes for a large store than what Melodee started with:

- exact normalized-name match is good
- exact reversed-name match is good
- exact alias lookup is the correct structure for alternate names on a large file
- exact `MusicBrainzIdRaw` is still suspicious enough to deserve targeted validation
- album fan-out by `MusicBrainzArtistId` is good as long as the artist set is small
- the old substring `AlternateNames.Contains(...)` path was not kept on the default large-file
  request path
- the old tokenized `Contains(...)` fallback was intentionally removed from that path

## Workload 2: local artist search cache / "media artists"

The local search-engine cache is backed by `ArtistSearchEngineServiceDbContext`.

This database stores artist and album data discovered from external providers and is used by:

- `ArtistSearchEngineService`
- the housekeeping refresh job
- CRUD/list operations for cached artist records
- doctor connectivity checks

This database is not the same scale as the MusicBrainz materialized database. That matters.

Melodee also has multiple protections in front of it:

- `ArtistSearchCache` caches positive and negative lookup results
- `DirectoryRunContext.ArtistSearchCache` adds per-run single-flight caching
- the search engine plugin order checks local Melodee data before MusicBrainz

That means the local cache database is already acting as a pressure-relief valve that keeps
many requests away from the large MusicBrainz file.

This is good architecture and should be preserved.

## Probe results against the real 2.31 GB MusicBrainz file

I ran a focused probe against the real database file and measured both generic EF access and
query shapes that closely resemble Melodee's actual MusicBrainz repository logic.

### Historical real-file timings that drove the request-path fixes

| Operation | Cold | Warm | Assessment |
| --- | ---: | ---: | --- |
| `DbContextOptions` build | ~22 ms | n/a | Fine |
| `new MusicBrainzDbContext(...)` | ~1 ms | ~0 ms | Fine |
| `context.Model` access | ~12 ms | ~0.4 ms | Fine |
| `Database.CanConnectAsync()` | ~13 ms | ~3 ms | Fine |
| `Artists.AnyAsync()` | ~13.3 s | ~13.0 s | Bad |
| `Artists.Take(1).CountAsync()` | ~13.3 s | ~13.0 s | Bad |
| `Artists.Select(x => x.Id).FirstOrDefaultAsync()` | ~15 ms | ~7 ms | Good |
| MusicBrainz exact normalized-name flow | ~40 ms | ~13 ms | Good |
| MusicBrainz alternate-name substring search | ~15.3 s | ~15.1 s | Historical red flag |
| MusicBrainz tokenized `Contains(...)` fallback | ~22.3 s | ~22.4 s | Historical red flag |
| MusicBrainz exact-MBID flow | ~34.6 s | ~34.2 s | Red flag |

### What these timings mean

These numbers show that the large `.ddb` file is not inherently too slow for Melodee.

The store can absolutely support request-path usage when the query shape is compatible with:

- existing indexes
- first-row short-circuiting
- small bounded result sets

The store becomes a problem when the query shape implies:

- full or near-full scans
- non-short-circuiting existence checks
- substring searches on large string columns
- query plans that do not make effective use of equality indexes

That distinction is the core lesson for Melodee and for the DecentDB EF provider work.

These measurements also matter historically even though some of those paths are no longer the
current design. They explain why the request path was changed:

- the alternate-name substring path was replaced structurally rather than micro-optimized
- the tokenized fallback was removed from the normal large-file path
- the exact MBID path was left under explicit suspicion until rebuilt-db validation can confirm it

## MusicBrainz use cases: what is safe and what is not

### 1. Dashboard doctor and health checks

### Current verdict

**Healthy now, after the Melodee-side fix.**

### Why

`DoctorService` now checks MusicBrainz health with:

- `Database.CanConnectAsync()`
- `Select(x => x.Id).FirstOrDefaultAsync()`

That is exactly the right pattern for a large file-backed DecentDB table.

This should remain the rule for all request-path existence checks against large DecentDB
tables:

- do not use `AnyAsync()` for "does at least one row exist?"
- do not use `CountAsync()` for "does at least one row exist?"
- do use a projected first-row fetch such as `Select(Id).FirstOrDefaultAsync()`

Even if the provider continues to improve, Melodee should keep this pattern because it is
the most explicit and least surprising shape for large stores.

### 2. Exact normalized-name artist lookup

### Current verdict

**Good.**

### Why

The `Artist` materialized model has an index on `NameNormalized`, and the probe showed the
exact normalized-name flow completing in tens of milliseconds, even against the 2.31 GB file.

That means Melodee's first and most important MusicBrainz search phase is already aligned
with the large-file reality.

The reversed-name phase reuses the same exact-match pattern, so it should have the same
general performance characteristics.

### 3. Album fan-out after artist match

### Current verdict

**Good, if kept bounded.**

### Why

After finding a small artist set, Melodee loads albums by:

- collecting `MusicBrainzArtistId` values
- issuing a bounded `Contains(...)` query on `Albums`
- relying on the `MusicBrainzArtistId` index

This is fine because:

- the repository intentionally limits the search result set
- the album lookup is downstream of a selective artist match
- the `Album` materialized entity has an index on `MusicBrainzArtistId`

This is a good pattern for DecentDB: narrow first, then fan out.

### 4. Alternate-name lookup

### Current verdict

**Now on the correct structural path, but rebuilt databases are required to benefit fully and the
final post-rebuild real-file timing is still outstanding.**

### Why

The old query shape was:

- `AlternateNames != null && AlternateNames.Contains(query.NameNormalized)`

On the large file, that substring scan took about fifteen seconds. That was not acceptable for
interactive lookup.

Melodee no longer has to rely on that design for the normal large-file request path.

Instead, Melodee now materializes normalized aliases into a dedicated lookup structure and resolves
exact alias matches there. That is the right large-file shape because it turns alternate-name lookup
back into an equality lookup rather than a blob substring scan.

### Root cause

The original root cause was structural:

- `AlternateNames` was stored as a single string blob
- the lookup was a substring scan over that blob
- there was no realistic indexing strategy that made that shape attractive on a huge file

The delivered fix was also structural:

- materialize one normalized alias per lookup row
- index the alias lookup column
- keep the request path on exact-match semantics

### Recommendation

Keep substring scanning over `AlternateNames` off the normal request path for the large
MusicBrainz database.

Preferred options:

1. keep the new normalized alias lookup structure as the canonical online fallback
2. rebuild existing databases so the alias lookup structure is actually populated
3. if fuzzy alias search is needed later, give it a separate opt-in structure and cost model
4. do not fall back to blob substring scans silently on the large-file request path

### 5. Tokenized `Contains(...)` fallback

### Current verdict

**Intentionally removed from the default large-file request path.**

### Why

The old tokenized fallback issued substring searches like:

- `NameNormalized.Contains(word)`
- `AlternateNames.Contains(word)`

The probe showed roughly twenty-two seconds for this shape on the large file. That was worse than
the already-bad alternate-name substring path and clearly outside acceptable interactive latency.

This is exactly the kind of search phase that looks user-friendly in code review but quietly
guarantees scan-heavy behavior on a very large file-backed store.

### Recommendation

Keep the tokenized `Contains(...)` phase out of the normal request path for the large MusicBrainz
DecentDB file.

If Melodee wants fuzzy fallback behavior here, it should come from:

- a separate alias/token table
- a precomputed search index
- an explicit "slow fallback" mode that is opt-in and off the main UI path

### 6. Exact MusicBrainz ID lookup

### Current verdict

**Unexpectedly slow; treat as a red-flag concern until explicitly verified.**

### Why

This path should have been one of the best-performing paths because:

- it is an equality lookup
- `MusicBrainzIdRaw` is indexed
- the result set is tiny

Instead, the probe showed the exact-MBID flow taking roughly thirty-four seconds.

That is not a normal result and should be investigated as a provider/schema/data issue rather
than accepted as normal workload behavior.

### Additional concern

While probing, a sampled `MusicBrainzIdRaw` value did not round-trip as a printable GUID string
in console output. I have not yet proven whether the cause is:

- provider string decoding
- imported data representation
- console rendering of returned bytes

However, when that observation is combined with the thirty-four-second exact-MBID lookup time,
`MusicBrainzIdRaw` becomes a high-priority verification area.

### Recommendation

Before Melodee leans harder on MBID-exact lookup, validate all of the following:

1. the stored DecentDB column type for `MusicBrainzIdRaw`
2. the data produced by the importer for that column
3. provider string round-tripping for that column
4. index usage/effectiveness for equality on that column
5. whether the current `.ddb` file was created before an importer/provider fix and needs
   regeneration

Until that is resolved, exact-name lookup is more trustworthy than exact-MBID lookup in the
observed file.

### 7. Cover-art lookup through MusicBrainz

### Current verdict

**Mostly acceptable, but it inherits the strengths and weaknesses of `SearchArtist(...)`.**

### Why

`MusicBrainzCoverArtArchiveSearchEngine` uses `SearchArtist(...)` with artist and album context
and then extracts the release-group ID for Cover Art Archive.

That means:

- if the query resolves through exact normalized-name match, it is fine
- if it resolves through the new exact alias lookup path, it should also be fine once the rebuilt
  database has populated that structure
- the old tokenized `Contains(...)` fallback is no longer supposed to be part of the default path
- if it relies on the exact-MBID path that is currently suspicious, it can also become slow

### Recommendation

Keep cover-art lookup dependent on the indexed search phases as much as possible. Do not reintroduce
slow fallback search behavior here silently.

## MusicBrainz import/update path

### Current verdict

**Viable as an offline workflow after multiple Melodee-side and provider-side fixes. Synthetic
validation is green again, but final full-file completion metrics are still outstanding.**

### What is already good

The importer is not trying to use high-level EF for every row-level operation. It uses:

- staging tables
- batched streaming
- raw SQL for materialization
- direct command execution for alias updates
- helper-table precomputation for the hot album join path
- prepared ADO inserts for the write-heavy materialization phases

That is the right general shape for a large import pipeline.

### Real rebuild findings

I ran the import path against the real staged MusicBrainz dump already present under:

- `/mnt/incoming/melodee_test/search-engine-storage/musicbrainz/staging`

That staging footprint was already substantial:

- `mbdump.tar.bz2`: ~6.43 GB
- `mbdump-derived.tar.bz2`: ~455.62 MB
- extracted `mbdump/`: ~23.94 GB
- total staging footprint: ~30.81 GB

The first live rebuild attempt did **not** fail because DecentDB could not open or write the
large `.ddb` file. Instead, it exposed a Melodee importer algorithm problem during album
materialization.

Observed timeline from the first live run:

- `11:49:05` - streaming artist file began
- `11:57:02` - streamed `808,816` artist links to staging
- `12:08:28` - materialized `2,792,908` artists
- `12:09:04` - materialized `7` artist relations
- `12:12:25` - artist staging cleared and album-side staging began
- `13:04:51` - streamed `5,265,580` releases and started album materialization

At that point the process became the bottleneck:

- RSS climbed to roughly `20 GB`
- the `.ddb` size plateaued at about `7.12 GB`
- sampled `/proc/<pid>/io` counters stopped showing meaningful additional file I/O
- CPU remained heavily utilized

That signature is important. It means the importer was mostly burning CPU and memory inside the
application process rather than being blocked on DecentDB file open or write throughput.

That first run identified the original failure mode, but it was not the end of the investigation.
After the first streaming/batching fixes were applied, the importer became functionally correct
again and memory behavior improved, yet the performance tests were still failing badly. That led to
phase-level profiling of the importer itself.

Phase probe results from the temporary diagnostic harness showed:

- synthetic dataset: `1000` artists with `5` albums each
- total import time: about `69.57s`
- `Loading Artists`: about `0.30s`
- `Materializing Artists`: about `0.35s`
- `Loading Albums`: about `2.36s`
- `Materializing Albums`: about `63.54s`

That result narrowed the remaining bottleneck to the album phase.

The next probe split album materialization into query time versus insert time:

- album query time: about `65.75s`
- album insert time: about `0.34s`

That was the decisive result. Once album inserts were made cheap, the remaining hotspot was the
album materialization query shape itself, not the write loop.

### Root cause

The root cause turned out to be layered, not singular.

1. **Initial failure mode**
   - `DecentDBStreamingMusicBrainzImporter.MaterializeAlbumsAsync(...)` was reading the entire
     album join result into one huge `List<Album>` before writing that batch to the database.
   - For a real dump containing millions of releases, that creates avoidable allocation pressure,
     delays persistence too long, and magnifies correctness issues such as duplicate valid
     `release_country` rows.

2. **Second-order bottleneck after the first memory fix**
   - Once writes were made bounded and cheap, the hot album query still performed very poorly.
   - The expensive parts were the grouped/derived release-country resolution and the primary-artist
     lookup shape inside the album materialization query.
   - In other words, the importer no longer had just a write-path problem. It also had a
     planner-unfriendly query-shape problem.

3. **Provider-side write overhead that was worth fixing, but was not the main bottleneck**
   - repeated single-statement `ExecuteNonQuery()` calls were reparsing and re-preparing every time
     before the provider change
   - fixing that was the right DecentDB ADO improvement, but the phase probe proved it was not the
     dominant remaining import cost once the album query was isolated

### Fix delivered

The importer and provider now include the following delivered fixes:

1. **Correct raw staging parameter binding**
   - while converting staging writes to raw commands, a regression was found:
     `$p0`, `$p1`, ... placeholders were not recognized by the provider's parameter rewriter
   - the raw staging helper was corrected to use `@p0`, `@p1`, ... placeholders instead

2. **Prepared raw staging and album inserts**
   - raw staging commands now call `Prepare()`
   - album persistence moved to prepared ADO inserts with lightweight row buffering

3. **Provider prepared statement reuse**
   - `DecentDBCommand.ExecuteNonQuery()` now reuses prepared statements for repeated
     single-statement non-query execution
   - `Prepare()` now primes that path instead of being a no-op

4. **Artist alternate-name materialization**
   - the importer no longer performs a per-artist C# update loop to build alternate names
   - bulk SQL aggregation is used instead

5. **Album materialization batching**
   - keyset pagination on `ReleaseStaging.Id`
   - bounded `LIMIT`-based reads per batch
   - release-country selection collapsed to the first valid row per release before album creation
   - progress messages emitted after each persisted batch

6. **Album query simplification**
   - precompute `ReleaseCountryResolvedStaging`
   - precompute `ArtistCreditPrimaryArtistStaging`
   - join those keyed helper tables from the album materialization query instead of repeatedly
     resolving those relationships in the hot query itself

This does two things:

1. it keeps memory bounded during the album phase
2. it removes a substantial amount of unnecessary work from the dominant album query path

Focused validation was added or rerun to lock the important behavior in place:

- `ImportAsync_WithDuplicateReleaseCountries_MaterializesSingleAlbumPerRelease`
- direct DecentDB ADO tests for prepared statement reuse
- importer suite validation, with the latest `StreamingMusicBrainzImporterTests` run passing
  `11/11` in `26s`

### Latest clean monitored rerun status

After the synthetic validation went green again, a clean real-file rerun was started from a
compiled `Release` probe harness so the monitor data would represent importer work rather than
`dotnet run` build time. That rerun was intentionally stopped at user request before completion so
the session could be wrapped up.

What was completed before stopping:

- the existing `musicbrainz.ddb` and `musicbrainz.ddb-wal` were deleted before the rerun
- the rerun was launched from the compiled harness DLL
- the monitored root PID was `1908758`
- the importer had cleanly started and was still in the artist-file staging phase when stopped

Partial metrics captured before stopping:

- last sampled elapsed time: `00:08:00`
- last sampled total RSS: `180,948 KB`
- sampled peak `VmHWM`: `186,964 KB`
- sampled total CPU time: `478.61s`
- last sampled `.ddb` size: `591,761,408` bytes
- last sampled `.wal` size: `42,372,368` bytes
- on-disk sizes immediately after stopping:
  - `.ddb`: `615,456,768` bytes
  - `.wal`: `84,744,736` bytes

That partial rerun is not a substitute for final completion metrics, but it is still useful:

- it shows the cleaned-up rerun starts correctly
- it shows healthy early-stage `.ddb`/`.wal` growth
- it shows sampled memory remaining dramatically below the earlier pre-fix high-memory plateau

### Concerns

There are still a few areas to watch:

1. `ImportData(...)` finishes by calling `CountAsync()` on `Artists` and `Albums`. That is fine
    for an offline job, but it is still expensive work on huge tables and does not belong on a
    request path.
2. the fully representative CLI job path is still harder to validate end-to-end than the direct
    repository harness path in this shell environment because the PostgreSQL-backed configuration
    store was unreachable from the test shell.
3. the clean monitored rerun was intentionally paused at user request after about eight minutes of
   artist-file staging, so the final full-run metrics are still missing.
4. `MusicBrainzIdRaw` exact lookup is still suspicious enough that it should be re-measured on the
   rebuilt database before it is treated as a strong fast path.

### Recommendation

For the import pipeline, prefer:

- offline-only counts or logged inserted-row counts already known from materialization steps
- batch-limited materialization for every very-large table fan-out phase, not just album writes
- helper-table precomputation when a hot join repeatedly has to rediscover the same small keyed
  relationships
- direct perf logging around major import phases so memory and I/O regressions are obvious early
- compiled-harness monitoring for real rebuild validation so build time and worker time are not
  conflated

This is not the top priority compared with request-path search behavior, but it is worth
tracking.

## Local artist search cache ("media artists") use cases

### 1. Why this database is in a better place than MusicBrainz

The local artist search cache is not being asked to behave like the 2.31 GB MusicBrainz file.
That matters more than any single query detail.

Melodee already reduces pressure on this database through:

- in-memory `ArtistSearchCache`
- per-run `SingleFlightCache` in `DirectoryRunContext`
- plugin ordering that checks Melodee/local data before MusicBrainz

That means Melodee usually pays the large-file MusicBrainz cost only when the faster sources do
not already have the answer.

Architecturally, that is the correct direction.

### 2. Local cache search path

### Current verdict

**Acceptable today, but should be hardened before this database grows much larger.**

### Why

The local cache search uses multiple indexed equality lookups:

- `NameNormalized`
- `MusicBrainzId`
- `SpotifyId`
- various provider IDs with unique indexes

Those are fine.

However, the query also mixes those indexed predicates with:

- `AlternateNames.Contains(...)`
- a wide `OR` chain
- eager `Include(x => x.Albums)`

That combination is survivable for a small working set but is not something I would want to
trust blindly once the local cache becomes large.

### Recommendation

For future-proofing, split the local cache search into staged phases the same way MusicBrainz
search should be split:

1. exact ID lookups
2. exact normalized-name lookup
3. exact alternate-name token lookup via a normalized alias structure
4. only then consider slower fallbacks

That keeps the fast path obviously index-driven.

### 3. Local cache admin listing

### Current verdict

**Fine for a small cache, but contains scale risks.**

### Why

`ArtistSearchEngineService.ListAsync(...)` always does:

- `CountAsync()` for total rows
- pagination with `Skip/Take`
- a correlated album count per projected artist row

That is okay for a modest working set and likely only exercised in admin tooling, but it is not
the shape I would choose if this database becomes large.

### Recommendation

If the local cache grows materially, revisit this method:

- make total count optional when the UI does not strictly need it
- replace per-row correlated album counts with a precomputed counter or grouped query
- keep ordering aligned with indexed columns whenever possible

This is not urgent in the same way the large MusicBrainz fallback scans are urgent, but it is
the most obvious local-cache scale concern.

### 4. Housekeeping job

### Current verdict

**Improved and in a reasonable place today.**

### Why

`ArtistSearchEngineRepositoryHousekeepingJob` filters on:

- `IsLocked`
- `LastRefreshed`

and orders by `LastRefreshed`.

Earlier in the session, this was one of the obvious scale mismatches in the local cache workflow.
That gap has now been addressed by ensuring the housekeeping-supporting index exists even on
already-created DecentDB files.

### Recommendation

Keep the housekeeping index in place and only revisit the exact shape if the local cache workload
changes materially. If the cache grows much larger, a composite `(IsLocked, LastRefreshed)` shape
may still be worth measuring explicitly.

### 5. EnsureCreated and doctor checks on the local cache DB

### Current verdict

**Healthy.**

### Why

The local cache DB is only checked for connectivity in doctor paths and is initialized with
`EnsureCreatedAsync()` during service startup. Those are reasonable operations for this smaller
database.

No major concern here.

## What Melodee is already doing right

Melodee is already doing several things that should be kept:

1. **Separate databases for separate workloads**
   - MusicBrainz is isolated from the local cache.

2. **Bounded MusicBrainz result sets**
   - `SearchArtist(...)` internally caps search result size.

3. **Caching around search**
   - repeated searches are not forced back through the same path every time.

4. **Local-first search ordering**
   - Melodee's own data is consulted before the large MusicBrainz database.

5. **Doctor fast-path fix**
   - first-render health checks now use an appropriate query shape.

6. **Offline-oriented import pipeline**
   - bulk import is using staging + SQL materialization instead of naive row-by-row EF inserts.

These are the correct building blocks. The remaining work is mostly about making the query
shapes match the scale of the MusicBrainz file.

## Recommended improvements by owner

### Melodee-side improvements

| Status | Priority | Area | Recommendation |
| --- | --- | --- | --- |
| Done | High | MusicBrainz request path | Keep `Select(Id).FirstOrDefaultAsync()` as the standard existence probe for large tables. |
| Done | High | MusicBrainz search | Keep substring/tokenized fallback phases off the normal large-file request path. |
| Done | High | MusicBrainz schema/search design | Keep the normalized alias lookup structure as the online alternate-name path; rebuilt databases are required to populate it. |
| Done | High | MusicBrainz import materialization | Keep album materialization batch-bounded and keep the helper-table query simplification in place. |
| Outstanding | High | MBID exact search | Treat `MusicBrainzIdRaw` lookup as suspect until verified against the rebuilt database and provider/data file. |
| Outstanding | Medium | Local cache search | Split local cache search into staged exact-match phases instead of one wide OR query. |
| Done | Medium | Local cache housekeeping | Maintain the new housekeeping index and revisit the exact composite shape only if the workload grows materially. |
| Outstanding | Medium | Local cache listing | Revisit `CountAsync()` and correlated album-count projections if the local cache grows substantially. |
| Outstanding | Low | Import completion | Prefer pre-known inserted counts over end-of-job `CountAsync()` when possible. |

### DecentDB provider / binding improvements

| Status | Priority | Area | Recommendation |
| --- | --- | --- | --- |
| Done | High | Native loading / TIMESTAMP correctness | Keep the local-source native asset fix, true `TIMESTAMP` mapping, and microsecond timestamp decoding in place. |
| Done | High | Provider guidance / existence semantics | Keep the `Any()` optimization work and continue documenting that large-table request paths should still prefer explicit first-row projection. |
| Done | Medium | UUID string compatibility | Preserve canonical GUID string reads for UUID-backed values read as strings. |
| Done | Medium | Repeated non-query execution | Keep prepared statement reuse for repeated single-statement `ExecuteNonQuery()` and the now-real `Prepare()` path. |
| Outstanding | High | Equality on indexed strings | Verify index usage and string round-tripping for large indexed string columns such as `MusicBrainzIdRaw`. |
| Outstanding | Medium | Large-text search | Decide whether the provider should support additional indexing/search strategies for substring-heavy workloads, or clearly document that these require a different schema. |
| Outstanding | Medium | Diagnostics | Add easy SQL/perf diagnostics so large-file plan problems are visible sooner in app-level probes. |

## Will Melodee's current DecentDB use cases be successful and performant?

### Short answer

**Yes for the key implemented paths, with two important caveats: the rebuilt real-file rerun still
needs final completion metrics, and exact `MusicBrainzIdRaw` lookup still needs direct validation.**

### Use-case verdict matrix

| Use case | Verdict | Notes |
| --- | --- | --- |
| Blazor dashboard doctor | **Success / performant** | Now uses a fast first-row projection. |
| CLI doctor DecentDB connectivity | **Success / performant** | Uses `CanConnectAsync()` only. |
| MusicBrainz exact normalized-name search | **Success / performant** | Real-file probe shows low-millisecond to tens-of-milliseconds performance. |
| MusicBrainz reversed-name exact search | **Likely success / performant** | Same query family as exact normalized-name. |
| MusicBrainz alternate-name lookup | **Success / structurally improved** | Now uses the normalized alias lookup path; rebuilt DBs are required and final post-rebuild timing is still outstanding. |
| MusicBrainz tokenized `Contains(...)` fallback | **Not on the default path** | Intentionally removed from the large-file request path. |
| MusicBrainz exact MBID lookup | **Questionable** | Probe showed ~34 seconds and suspicious raw-value behavior. Needs explicit validation. |
| MusicBrainz cover-art lookup | **Conditionally performant** | Good if exact name/alias phases hit; still questionable if it falls to the suspicious exact-MBID path. |
| Local artist cache search | **Success / acceptable today** | Smaller DB, cached, local-first. Still has scale concerns. |
| Local artist cache admin listing | **Success / acceptable today** | Count and correlated album count should be watched as the cache grows. |
| Local cache housekeeping | **Success / improved** | Housekeeping index is now ensured; revisit only if workload changes materially. |
| MusicBrainz import/update job | **Success / synthetic validation green** | Batch/query fixes are in place and importer tests now pass; final full-file completion metrics are still outstanding. |

## Recommended next steps

### Immediate next steps

1. Resume the clean monitored real-file rerun and let it finish so this document can carry final
   wall-clock, RSS, file-growth, and post-import probe metrics instead of only partial numbers.
2. Re-measure exact `MusicBrainzIdRaw` lookup on the rebuilt database before treating MBID-exact
   search as a strong fast path.
3. Keep the doctor-query, normalized alias, and tokenized-fallback removals in place. Those are
   now part of the intended design, not temporary experiments.
4. Record the final monitored rerun metrics back into this document and compare them directly with
   the earlier bad run.

### Near-term implementation candidates

1. Refactor local cache search into exact-match phases instead of one mixed OR query.
2. Add a committed perf regression probe for the query shapes that matter:
   - exact normalized-name search
   - exact MBID search
   - first-row projection existence check
   - exact alias lookup
   - any explicit slow fallback mode, if one is reintroduced later
3. Add a committed import perf regression probe for:
    - artist materialization
    - album materialization
    - peak RSS
    - final `.ddb` size growth over time
4. Add easier provider/app diagnostics for large-file query shape investigation.

## Final assessment

Melodee does **not** need to abandon DecentDB.EntityFrameworkCore for the large MusicBrainz file.
The work completed in this session actually strengthens that conclusion. Once the query shapes and
provider behavior are corrected, the large file is usable and performant for the paths that are
supposed to be interactive.

The core issue is not "DecentDB cannot handle a large MusicBrainz file." The core issue is:

> Melodee and the .NET bindings both needed targeted, scale-aware fixes. Once those were applied,
> the remaining problem narrowed to finishing real-file validation rather than searching for the
> root cause.

What is completed now:

- doctor checks are on the right path
- exact-name and exact-alias lookup design is on the right path
- tokenized substring fallback is off the normal request path
- TIMESTAMP support in the provider is fixed properly
- prepared non-query execution is better
- the importer is no longer designed around giant in-memory album buffering
- the hot album query has been simplified structurally
- synthetic importer validation is green again

What is still outstanding is much smaller and better defined:

- finish one clean monitored real-file rerun to completion
- capture and record the final full-file metrics
- explicitly validate `MusicBrainzIdRaw` exact lookup on the rebuilt database

That is a much better place to end a session than where this started. The remaining risk is no
longer "we do not know what is wrong." The remaining risk is "we still need the last completion
numbers and one suspicious exact-match path verified."
