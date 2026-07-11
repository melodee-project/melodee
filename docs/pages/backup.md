---
title: Backup & Recovery
description: Back up and restore Melodee PostgreSQL data, persistent files, secrets, and generated search databases.
permalink: /backup/
tags:
  - backup
  - restore
  - operations
  - postgres
---

# Backup & Recovery

A usable Melodee backup combines a logical PostgreSQL dump with the persistent
application files and the configuration needed to mount them. Copying only the
container image is not a backup.

## What to Protect

| Data | Default location | Required? |
|------|------------------|-----------|
| PostgreSQL database | `melodee_db_data` | Yes |
| Published media and DecentDB files | `melodee_storage` | Yes |
| Inbound and Staging work | `melodee_inbound`, `melodee_staging` | If work is pending |
| Playlists and user images | `melodee_playlists`, `melodee_user_images` | Yes |
| Podcasts, themes, and templates | Corresponding named volumes | If used |
| ASP.NET data-protection keys | `melodee_data_protection_keys` | Recommended |
| Deployment configuration | `.env`, Compose files, proxy configuration | Yes |
| Logs | `melodee_logs` | Optional for recovery; useful for diagnosis |

The MusicBrainz and Artist Search DecentDB files live under Storage in the
default container layout. They can be rebuilt, but preserving them can save
substantial download and import time.

## Create a Backup

Choose a destination that is not inside a Melodee volume:

```bash
backup_dir="/srv/backups/melodee/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$backup_dir"
chmod 700 "$backup_dir"
```

### 1. Dump PostgreSQL

`pg_dump` produces a transactionally consistent logical backup while
PostgreSQL is running:

```bash
docker compose exec -T melodee-db \
  pg_dump --username=melodeeuser --dbname=melodeedb --format=custom \
  > "$backup_dir/postgres.dump"
```

Confirm the dump can be read:

```bash
docker compose exec -T melodee-db pg_restore --list \
  < "$backup_dir/postgres.dump" > /dev/null
```

Do not archive `/var/lib/postgresql/data` while PostgreSQL is running. A raw
database-volume copy is not a replacement for `pg_dump` unless it is made by a
database-aware snapshot procedure.

### 2. Stop Application Writes

Stop the application while archiving its file volumes. PostgreSQL can remain
running:

```bash
docker compose stop melodee.blazor
```

### 3. Archive Persistent Volumes

The helper below archives one Docker named volume at a time:

```bash
backup_volume() {
  volume_name="$1"
  archive_name="$2"
  docker run --rm \
    --volume "${volume_name}:/source:ro" \
    --volume "${backup_dir}:/backup" \
    alpine:3.20 \
    tar -C /source -czf "/backup/${archive_name}.tar.gz" .
}

backup_volume melodee_storage storage
backup_volume melodee_inbound inbound
backup_volume melodee_staging staging
backup_volume melodee_user_images user-images
backup_volume melodee_playlists playlists
backup_volume melodee_podcasts podcasts
backup_volume melodee_themes themes
backup_volume melodee_templates templates
backup_volume melodee_data_protection_keys data-protection-keys
```

If a path is a bind mount, archive or snapshot the host directory with your
normal backup system instead of using `docker volume`.

For Podman, replace `docker` with `podman` and confirm the volume names with
`podman volume ls`.

### 4. Save Configuration and Restart

```bash
cp --preserve=mode .env compose.yml "$backup_dir/"
if [ -f compose.override.yml ]; then
  cp --preserve=mode compose.override.yml "$backup_dir/"
fi
docker compose start melodee.blazor
curl --fail http://localhost:8080/health
```

Treat `.env` and database dumps as secrets. Encrypt backup media, restrict file
permissions, and do not commit them to Git.

### 5. Create Checksums

```bash
cd "$backup_dir"
sha256sum postgres.dump *.tar.gz > SHA256SUMS
sha256sum --check SHA256SUMS
```

Copy the completed backup to another failure domain. A backup on the same disk
as the live data does not protect against disk loss.

## Restore a Backup

Test restores on an isolated host or Compose project before relying on the
procedure in an emergency.

### 1. Stop Melodee

```bash
docker compose down
```

Do not use `-v`; the restore procedure reuses or deliberately replaces the
existing named volumes.

### 2. Restore File Volumes

Restore only into empty volumes, or explicitly remove their existing contents
after confirming the volume name and backup set.

```bash
restore_volume() {
  volume_name="$1"
  archive_path="$2"
  archive_name="$(basename "$archive_path")"
  docker volume create "$volume_name" > /dev/null
  docker run --rm \
    --volume "${volume_name}:/target" \
    --volume "$(dirname "$archive_path"):/backup:ro" \
    alpine:3.20 \
    sh -c 'rm -rf /target/* /target/.[!.]* /target/..?* 2>/dev/null || true; tar -C /target -xzf "/backup/$1"' \
    sh "$archive_name"
}

restore_volume melodee_storage "$backup_dir/storage.tar.gz"
restore_volume melodee_inbound "$backup_dir/inbound.tar.gz"
restore_volume melodee_staging "$backup_dir/staging.tar.gz"
restore_volume melodee_user_images "$backup_dir/user-images.tar.gz"
restore_volume melodee_playlists "$backup_dir/playlists.tar.gz"
restore_volume melodee_podcasts "$backup_dir/podcasts.tar.gz"
restore_volume melodee_themes "$backup_dir/themes.tar.gz"
restore_volume melodee_templates "$backup_dir/templates.tar.gz"
restore_volume melodee_data_protection_keys "$backup_dir/data-protection-keys.tar.gz"
```

The cleanup inside this example is destructive to the named target volume. Run
it only after verifying the target name and backup path. For bind mounts,
restore the host directories with the tool that created their backups.

### 3. Restore PostgreSQL

Start only the database service, replace the application database, and feed the
custom-format dump to `pg_restore`:

```bash
docker compose up -d melodee-db
docker compose exec -T melodee-db \
  dropdb --username=melodeeuser --if-exists --force melodeedb
docker compose exec -T melodee-db \
  createdb --username=melodeeuser melodeedb
docker compose exec -T melodee-db \
  pg_restore --username=melodeeuser --dbname=melodeedb \
  --no-owner --no-privileges \
  < "$backup_dir/postgres.dump"
```

Restore `.env` and Compose overrides before starting the application so the
database password, JWT signing key, paths, and image version match the backup.

### 4. Start and Verify

```bash
docker compose up -d
docker compose ps
docker compose logs --tail=250 melodee.blazor
curl --fail http://localhost:8080/health
```

Sign in, run **Admin > Doctor**, browse a known album, stream a song, and inspect
library counts. Keep the backup untouched until verification is complete.

## Backup Policy

A practical policy includes:

- Frequent PostgreSQL dumps
- File snapshots after imports or metadata curation
- A backup before every Melodee or PostgreSQL upgrade
- Multiple retention periods, such as daily, weekly, and monthly copies
- At least one encrypted off-host or offline copy
- Scheduled restore tests with recorded results

Volume archives can be large. If the canonical media library is already
protected by storage snapshots, do not duplicate it blindly; document exactly
which system is authoritative and ensure its restore is tested with the matching
PostgreSQL backup.
