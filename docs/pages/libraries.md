---
title: Libraries
description: Understand Melodee's media-ingestion libraries, supporting data paths, and current access-control status.
permalink: /libraries/
tags:
  - administration
  - libraries
  - storage
---

# Libraries

Libraries are filesystem roots registered in Melodee. Three library types form the music-ingestion pipeline; the other types hold application data such as podcast downloads, playlist definitions, and themes.

## Music lifecycle

```text
Inbound -> Staging -> Storage -> PostgreSQL catalog and search indexes
```

### Inbound

Drop one release per directory into the Inbound path. **LibraryInboundProcessJob** scans the directory, reads supported audio metadata, creates Melodee metadata, and moves processed releases into Staging.

The scanner recognizes `.mp3`, `.flac`, `.ogg`, `.m4a`, `.wav`, `.wma`, `.aac`, and `.opus` as media candidates. Successful processing still depends on readable tags and the configured validation rules.

### Staging

Staging is the review area. Albums here are not available to playback APIs. Review validation status, correct metadata or artwork, and approve albums before promotion.

**StagingAutoMoveJob** moves albums with an **Ok** status into the first configured Storage library. The administrator UI also has a **Move Ok** action.

### Storage

Storage is the canonical, playable music collection. **LibraryInsertJob** reads the Melodee metadata under Storage and inserts new artists, albums, and songs into PostgreSQL.

Storage is the only library type that can have multiple records. Use multiple Storage roots when media lives on different disks or mounts. Inbound, Staging, and each application-data type are restricted to one record by the database.

## Default libraries

A new database seeds these paths:

| Type | Default container path | Purpose |
|---|---|---|
| Inbound | `/app/inbound/` | New releases awaiting processing |
| Staging | `/app/staging/` | Processed releases awaiting approval |
| Storage | `/app/storage/` | Published music |
| User Images | `/app/user-images/` | User profile images |
| Playlist | `/app/playlists/` | Playlist images and file-defined dynamic playlists |
| Templates | `/app/templates/` | Email templates and [Custom Blocks](/custom-blocks/) |
| Podcast | `/app/podcasts/` | Downloaded podcast media and cover art |
| Theme | `/app/themes/` | Installed [themes](/theming/) |

The `compose.yml` file mounts each of those paths. Paths configured in Melodee are paths as seen by the application container, not arbitrary host paths.

The **Chart** type is available but is not seeded or mounted by the default Compose file. Add it only if you want custom chart images; see [Charts](/charts/#chart-images).

## Automated jobs

The default schedules are:

| Job | Default | Function |
|---|---|---|
| LibraryInboundProcessJob | Every 10 minutes | Process Inbound releases |
| StagingAutoMoveJob | Every 15 minutes | Move approved releases to Storage |
| LibraryInsertJob | Daily at 00:00 | Insert Storage metadata into the catalog |

When the scheduler starts Inbound processing, the jobs can chain through Staging and library insertion. A job started manually does not automatically run the next job, which makes it safer for review and troubleshooting. See [Background Jobs](/jobs/) for keys, schedules, and chaining details.

Do not assume a new release will be playable after only the Inbound job. Confirm that it reached Storage and that LibraryInsertJob completed.

## Manage libraries

Administrators manage records under **Libraries**:

- edit the name, application-visible path, type, description, notes, tags, and sort order;
- lock a library to prevent selected processing operations;
- add additional Storage roots;
- inspect scan history, statistics, validation results, and album status;
- run scans or maintenance actions.

Before changing a path:

1. mount or create the destination;
2. make it readable and writable by the Melodee process;
3. update the library record;
4. validate it with **Doctor** or `mcli library validate`;
5. run the appropriate scan job.

Changing a database path does not move existing files.

## Access-control preview

The library editor can associate a library with user groups, and Melodee stores those relationships. In 2.2.0, media queries and streaming controllers do not call the library-authorization service, so these group assignments do **not** yet restrict search, browsing, or playback.

Treat library-group access as a preview configuration surface. Do not use it as a security boundary or for multi-tenant isolation. Enforce separation with distinct Melodee instances or storage/network controls until authorization is connected throughout the media stack.

## Storage and backup guidance

- Keep Inbound, Staging, and Storage on the same filesystem when possible; promotion can then use fast moves instead of cross-device copies.
- Keep PostgreSQL on durable local storage. It is the primary database; DecentDB files are generated search-engine data.
- Back up PostgreSQL, Storage, and any manually curated application-data volumes.
- Inbound and Staging may be omitted only when you accept losing work in progress.
- Use filesystem snapshots or stop writes while taking a consistent media backup.

See [Backup and Restore](/backup/) for a complete backup order and restore procedure.

