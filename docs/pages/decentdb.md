---
title: DecentDB Usage & Migration
description: How Melodee uses DecentDB, how to diagnose compatibility issues, and how to rebuild generated DecentDB search databases.
permalink: /decentdb/
tags:
  - decentdb
  - migration
  - troubleshooting
---

## Overview

Melodee uses DecentDB for local, generated search databases. These files are
separate from the primary Melodee PostgreSQL database and can be rebuilt from
source data when necessary.

DecentDB is currently used for:

- **MusicBrainz search data**: the local MusicBrainz artist, alias, relation,
  and album lookup database.
- **Artist search cache**: the local artist search repository used to speed up
  artist matching and enrichment.

Melodee does not store user accounts, playlists, play history, ratings, or
library metadata in these DecentDB files. Those records live in the primary
PostgreSQL database.

## Configuration

The DecentDB databases are configured with these connection strings:

| Connection string | Purpose |
|-------------------|---------|
| `MusicBrainzConnection` | Local MusicBrainz lookup database |
| `ArtistSearchEngineConnection` | Local artist search cache database |

In container deployments, the same values can be supplied with environment
variables:

```bash
ConnectionStrings__MusicBrainzConnection="Data Source=/app/storage/_search-engines/musicbrainz/musicbrainz.ddb"
ConnectionStrings__ArtistSearchEngineConnection="Data Source=/app/storage/_search-engines/artistSearchEngine.ddb"
```

Use your configured paths as the source of truth. Older installations may use
`.db` filenames while newer examples use `.ddb`; the connection string path is
what matters.

## Doctor Compatibility Checks

Doctor opens both DecentDB files with the current Melodee DecentDB provider. If
the file was created by a newer or incompatible DecentDB engine, Doctor reports
an issue similar to:

```text
MusicBrainz DecentDB database uses a file format that is not supported by the current DecentDB provider.
Provider error: unsupported DecentDB file format version 11
```

This means the current Melodee process cannot safely read that generated search
database. Search and enrichment may continue in degraded mode, but the affected
database should be rebuilt or the Melodee/DecentDB package version should be
updated.

## Migration Strategy

For Melodee's generated DecentDB files, migration usually means rebuilding the
affected generated database with the current Melodee version. This is safer than
trying to manually edit a DecentDB file created by an incompatible provider.

Use this order:

1. Upgrade Melodee to the intended version.
2. Stop Melodee so no process is writing to the DecentDB files.
3. Back up the affected `.ddb` file and any companion WAL or shared-memory
   files.
4. Move the incompatible generated database out of the active path.
5. Start Melodee.
6. Rebuild the affected database from Melodee.
7. Run Doctor again and confirm the DecentDB checks pass.

## Backup Before Rebuild

Back up the main database and generated DecentDB files before changing anything.
The example below preserves common DecentDB companion file names.

```bash
#!/usr/bin/env bash
set -euo pipefail

backup_root="$HOME/melodee-decentdb-backup-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$backup_root"

musicbrainz_db="/path/to/search-engine-storage/musicbrainz/musicbrainz.ddb"
artist_search_db="/path/to/search-engine-storage/artistSearchEngine.ddb"

backup_decentdb_file() {
  local db_path="$1"
  local db_name
  db_name="$(basename "$db_path")"

  for suffix in "" ".wal" "-wal" ".shm" "-shm"; do
    if [ -f "${db_path}${suffix}" ]; then
      cp -a "${db_path}${suffix}" "$backup_root/${db_name}${suffix}"
    fi
  done
}

backup_decentdb_file "$musicbrainz_db"
backup_decentdb_file "$artist_search_db"

echo "Backed up DecentDB files to $backup_root"
```

Also back up PostgreSQL before a Melodee upgrade. See
[Backup & Recovery](/backup/) for full backup guidance.

## Example: Rebuild Incompatible DecentDB Files

This example renames incompatible generated DecentDB files so Melodee can create
fresh files with the current provider.

```bash
#!/usr/bin/env bash
set -euo pipefail

stamp="$(date +%Y%m%d-%H%M%S)"

musicbrainz_db="/path/to/search-engine-storage/musicbrainz/musicbrainz.ddb"
artist_search_db="/path/to/search-engine-storage/artistSearchEngine.ddb"

move_decentdb_file_aside() {
  local db_path="$1"

  for suffix in "" ".wal" "-wal" ".shm" "-shm"; do
    if [ -f "${db_path}${suffix}" ]; then
      mv "${db_path}${suffix}" "${db_path}${suffix}.unsupported-$stamp"
    fi
  done
}

# Stop Melodee before moving active database files.
# podman compose down
# docker compose down

move_decentdb_file_aside "$musicbrainz_db"
move_decentdb_file_aside "$artist_search_db"

# Start Melodee again.
# podman compose up -d
# docker compose up -d
```

After the files are moved aside, rebuild the generated databases.

### MusicBrainz

Use one of these options:

- In the web UI, go to **Admin > Doctor** and use **Generate MusicBrainz
  Database**.
- In the web UI, go to **Admin > Jobs** and run
  `MusicBrainzUpdateDatabaseJob`.
- From the CLI, run:

```bash
./mcli job musicbrainz-update
```

MusicBrainz rebuilds can take a long time because Melodee downloads and imports
the MusicBrainz dump into a local DecentDB file.

### Artist Search Cache

Use one of these options:

- In the web UI, go to **Admin > Jobs** and run
  `ArtistSearchEngineRepositoryHousekeepingJob`.
- From the CLI, run:

```bash
./mcli job artistsearchengine-refresh
```

The artist search cache is generated from Melodee's library data and configured
artist search providers. It can be rebuilt after the incompatible file is moved
aside.

## Verify The Migration

Run Doctor after rebuilding:

```bash
./mcli doctor --verbose
```

Or open **Admin > Doctor** in the web UI.

The following checks should pass:

- `MusicBrainzDatabase`
- `ArtistSearchEngineDatabase`

If Doctor still reports an unsupported DecentDB file format after rebuilding,
confirm that:

- Melodee is running the version you expect.
- The active connection strings point to the rebuilt files.
- No old `.wal`, `-wal`, `.shm`, or `-shm` companion files remain beside the
  rebuilt database.
- The app container or service was restarted after the rebuild.

## What Not To Delete

Do not delete the primary PostgreSQL database when resolving DecentDB search
cache compatibility issues. The unsupported DecentDB file-format warning applies
to generated local search databases, not the primary Melodee database.

