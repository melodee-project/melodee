## Melodee DecentDB Package-Upgrade Validation Runbook

The runbook is Melodee-only and exists to validate candidate DecentDB
package upgrades against real MusicBrainz data without modifying service code.

## Scope

This runbook supports:

- Revalidating DDB-002 and DDB-003 on an existing checkpointed MusicBrainz `.ddb`.
- Rebuilding a fresh checkpointed file and collecting import/query probes when
  package changes require it.
- Producing artifacts that can be attached to upstream or downstream package
  regression issues.

Do **not** use the DecentDB CLI or local DecentDB repository changes in this
phase of validation.

## Required inputs

- `MUSICBRAINZ_DDB_PATH`: path to an existing checkpointed MusicBrainz file,
  for example `.tmp/decentdb-validation/musicbrainz.ddb`.
- `OUTPUT_DIR`: writable folder for probe artifacts.
- `PACKAGE_LABEL`: short identifier for the DecentDB package version being tested.

Optional inputs:

- `MUSICBRAINZ_STAGING_PATH`: path to a staging MusicBrainz dump for fresh
  import rebuild runs.
- `MUSICBRAINZ_SAMPLE_NAME`: normalized artist name from a prior probe.
- `MUSICBRAINZ_SAMPLE_ALIAS`: normalized artist alias from a prior probe.
- `MUSICBRAINZ_SAMPLE_MBID`: raw MusicBrainz artist ID from a prior probe.
- `MUSICBRAINZ_QUERY_JSON`: custom path for query probe output.
- `MUSICBRAINZ_IMPORT_JSON`: custom path for import probe output.

## Scripted execution

Use this helper:

```bash
./scripts/run-decentdb-package-upgrade-gate.sh \
  --db /path/to/musicbrainz.ddb \
  --label 2.13.1 \
  --output-dir .tmp/decentdb-upgrade \
  --name "$MUSICBRAINZ_SAMPLE_NAME" \
  --alias "$MUSICBRAINZ_SAMPLE_ALIAS" \
  --mbid "$MUSICBRAINZ_SAMPLE_MBID"
```

Optional fresh rebuild path:

```bash
./scripts/run-decentdb-package-upgrade-gate.sh \
  --db /tmp/musicbrainz.ddb \
  --storage /mnt/fileserver_db_storage/melodee/search-engine-storage/musicbrainz \
  --label 2.13.1-candidate \
  --output-dir .tmp/decentdb-upgrade
```

## Command sequence (manual equivalent)

1. Build once before repeated probes:

```bash
dotnet restore Melodee.sln
dotnet build Melodee.sln --no-restore
```

2. Run optional import probe for a fresh file (skip when using an existing
   checkpointed `.ddb`):

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks -- \
  musicbrainz-import-probe \
  --storage /path/to/musicbrainz \
  --db /tmp/musicbrainz.ddb \
  --output /tmp/musicbrainz-import-probe.json \
  --clean \
  --sample-interval-ms 10000
```

3. Run query probe on the checkpointed target:

```bash
dotnet run -c Release --project benchmarks/Melodee.Benchmarks -- \
  musicbrainz-query-probe \
  --db /tmp/musicbrainz.ddb \
  --output /tmp/musicbrainz-query-probe-checkpointed.json \
  --name "$MUSICBRAINZ_SAMPLE_NAME" \
  --alias "$MUSICBRAINZ_SAMPLE_ALIAS" \
  --mbid "$MUSICBRAINZ_SAMPLE_MBID"
```

Use `--include-row-existence` only while investigating broad table access. The
DDB-002/DDB-003 gate intentionally excludes that measurement by default because
it is not a request-safe search shape.

## Gate acceptance criteria

Gate output must satisfy all of these for DDB-002 and DDB-003:

| Item | Target |
| --- | --- |
| Targeted warm `Artist.NameNormalized` lookup | `IndexSeek` plan and non-timeout execution |
| Targeted warm `Artist.MusicBrainzIdRaw` lookup | `IndexSeek` plan and non-timeout execution |
| Warm execution time (both rows above) | Request-safe target (<= 1000 ms) |
| `Exact alias` behavior | Captured and materially faster than prior checkpointed regressions |
| Database open / probe report | Probe report is written and includes row counts |

This runbook intentionally keeps DDB-002/DDB-003 acceptance focused on
request-safe behavior against **checkpointed real-file** data rather than
WAL-backed-only outputs.

### Planned follow-up for non-hot-path search

- Do not use automatic fuzzy or substring matches on the large `Artist` table in hot
  request paths.
- Keep fuzzy/full-text behavior as explicit future work:
  bounded candidate extraction, separate indexed schema, and explicit cost
  guardrails.

## Checkpoint/WAL scheduling notes

- Perform checkpointing as a native maintenance step after import completion in
  Melodee; checkpointing is required before asserting DDB-002/DDB-003 request
  safety for production-scale data.
- Checkpointed artifacts should show expected WAL reduction and stable query timing
  before declaring a package-level PASS.
- If `.wal` remains large, run the query probe anyway for investigation, but
  require checkpointed/reopened artifacts for acceptance.
- Keep maintenance native to Melodee and DecentDB .NET bindings (`DecentDBMaintenance`
  helpers); avoid external CLI or repository-level changes.

Example maintenance contract:

```csharp
await DecentDBMaintenance.CheckpointAsync(databasePath, cancellationToken);
```

## Failure handling

If the gate fails:

- Keep existing DecentDB package versions unchanged in Melodee.
- Attach the produced import/query JSON, command output, and `dotnet` logs to the
  upstream ticket.
- Re-run the same named outputs on the next candidate package.
