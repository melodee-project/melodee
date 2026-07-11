---
title: Upgrade Guide
description: Back up, upgrade, verify, and if necessary restore a Melodee container deployment.
permalink: /upgrade/
tags:
  - upgrade
  - deployment
  - containers
  - migration
---

# Upgrade Guide

Melodee applies PostgreSQL migrations during startup. Generated DecentDB search
files have a separate on-disk format and may occasionally require the migration
procedure described in [DecentDB Usage & Migration](/decentdb/).

## Before You Upgrade

1. Record the running version:

   ```bash
   curl --fail http://localhost:8080/api/v1/system/info
   ```

2. Read the [changelog](/changelog/) and the release notes for every version you
   are crossing.
3. Create and verify a PostgreSQL dump and persistent-volume backup by following
   [Backup & Recovery](/backup/).
4. Save copies of `.env`, `compose.yml`, and any Compose override.
5. Record local changes with `git status`; do not overwrite an installation with
   uncommitted customizations.

## Published-Image Upgrade

For a pinned image, change `MELODEE_IMAGE` in `.env` to the desired tag. To
upgrade to 2.2.0:

```text
MELODEE_IMAGE=ghcr.io/melodee-project/melodee:2.2.0
```

Then pull and recreate only the application service:

```bash
docker compose pull melodee.blazor
docker compose up -d --no-build melodee.blazor
docker compose ps
```

Compose preserves all named volumes. Do not add `-v` to `docker compose down`
or use a volume-pruning command during an upgrade.

## Source-Build Upgrade

The setup helper's update mode rebuilds the checked-out source and recreates the
containers while preserving volumes:

```bash
cd /path/to/melodee
git pull --ff-only origin main
python3 scripts/run-container-setup.py --update
```

Use `--yes` only in automation where the branch, backup, and working tree have
already been validated:

```bash
python3 scripts/run-container-setup.py --update --yes
```

For a manual source build:

```bash
git pull --ff-only origin main
docker compose build --no-cache melodee.blazor
docker compose up -d melodee.blazor
```

## Verify the Upgrade

The first startup can take longer while migrations and initialization run.

```bash
docker compose ps
docker compose logs --tail=250 melodee.blazor
curl --fail http://localhost:8080/health
curl --fail http://localhost:8080/api/v1/system/info
```

Then sign in and run **Admin > Doctor**. Confirm:

- The reported Melodee version is the requested release.
- PostgreSQL migrations completed without errors.
- Library paths are mounted and writable.
- MusicBrainz and Artist Search DecentDB checks pass.
- A known album can be browsed and streamed.

If Doctor reports DecentDB error 8, stop the application service and use the
generated migration dialog or [DecentDB guide](/decentdb/). Do not delete the
primary PostgreSQL database to resolve a DecentDB format warning.

## Rollback and Restore

Rolling the application image backward after a PostgreSQL schema migration is
not guaranteed to be safe. A reliable rollback restores the application image,
PostgreSQL dump, and persistent files from the same pre-upgrade backup set.

1. Stop the application:

   ```bash
   docker compose stop melodee.blazor
   ```

2. Restore PostgreSQL and any changed persistent volumes by following
   [Backup & Recovery](/backup/#restore-a-backup).
3. Set `MELODEE_IMAGE` to the previous pinned version, then recreate the service:

   ```bash
   docker compose pull melodee.blazor
   docker compose up -d --no-build melodee.blazor
   ```

4. Repeat the health, version, Doctor, and playback checks.

Keep the failed upgrade logs and backup set until the restored installation has
been exercised successfully.
