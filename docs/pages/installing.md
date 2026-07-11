---
title: Installing
description: Install Melodee from a published container image, a local container build, or source.
permalink: /installing/
tags:
  - installation
  - containers
  - docker
  - podman
---

# Installing

Melodee is supported as a Linux AMD64 or ARM64 container. The application uses
PostgreSQL 17 for primary data and stores media and generated DecentDB search
files in persistent volumes.

## Choose an Installation Method

| Method | Best for | Builds locally? |
|--------|----------|-----------------|
| Published GHCR image | Most servers and homelabs | No |
| Container setup script | Contributors and customized images | Yes |
| Native source run | Development | Yes |

## Published Container Image

Release images are published to the
[Melodee package registry](https://github.com/orgs/melodee-project/packages).
The release workflow publishes these tags:

| Tag pattern | Meaning |
|-------------|---------|
| `latest` | Most recently published stable release |
| `2` | Most recent 2.x release |
| `2.2` | Most recent 2.2.x release |
| `2.2.0` | Immutable release version |
| `sha-...` | Image from a manually dispatched build |

Pin an exact version when repeatable deployments and rollbacks matter.

```bash
git clone https://github.com/melodee-project/melodee.git
cd melodee
cp example.env .env
```

Edit `.env`, replace every placeholder secret, and add:

```text
MELODEE_IMAGE=ghcr.io/melodee-project/melodee:2.2.0
```

Then pull and start the services:

```bash
docker compose pull melodee.blazor
docker compose up -d --no-build
docker compose ps
```

The web UI and all API surfaces listen on the host port defined by
`MELODEE_PORT`, which defaults to `8080`.

## Local Container Build

The supported setup helper checks available memory, disk, port, runtime, and
required files. It generates random secrets and offers to start the result:

```bash
python3 scripts/run-container-setup.py --start
```

Useful modes include:

```bash
# Validate the host without creating files
python3 scripts/run-container-setup.py --check-only

# Check named-volume permissions for rootless Podman
python3 scripts/run-container-setup.py --check-permissions

# Replace an existing generated .env after confirmation
python3 scripts/run-container-setup.py --force
```

For a manual local build:

```bash
cp example.env .env
# Edit .env before continuing.
docker compose up -d --build
```

The setup helper and `--build` workflow build the current checkout. They do not
pull the published Melodee application image.

## Persistent Storage

The default Compose file creates these named volumes:

| Volume | Container path | Contents |
|--------|----------------|----------|
| `melodee_db_data` | `/var/lib/postgresql/data` | PostgreSQL data files |
| `melodee_storage` | `/app/storage` | Published media and generated search databases |
| `melodee_inbound` | `/app/inbound` | New media awaiting processing |
| `melodee_staging` | `/app/staging` | Processed media awaiting promotion |
| `melodee_user_images` | `/app/user-images` | User images |
| `melodee_playlists` | `/app/playlists` | Playlist files |
| `melodee_podcasts` | `/app/podcasts` | Downloaded podcast media |
| `melodee_themes` | `/app/themes` | Imported theme packs |
| `melodee_templates` | `/app/templates` | Email templates and custom blocks |
| `melodee_logs` | `/app/Logs` | Application logs |
| `melodee_data_protection_keys` | `/home/melodee/.aspnet/DataProtection-Keys` | ASP.NET data-protection keys |

Use a Compose override to replace media volumes with host directories. Absolute
host paths are recommended:

```yaml
# compose.override.yml
services:
  melodee.blazor:
    volumes:
      - /srv/melodee/inbound:/app/inbound
      - /srv/melodee/staging:/app/staging
      - /srv/music:/app/storage
      - /srv/melodee/podcasts:/app/podcasts
      - /srv/melodee/themes:/app/themes
      - /srv/melodee/templates:/app/templates
```

Create the host directories first and ensure the container can read and write
them. Keep PostgreSQL on its named volume unless you have an established
database storage and backup design.

## Runtime Configuration

The default `compose.yml` requires:

- `DB_PASSWORD`: PostgreSQL password used by both services.
- `MELODEE_AUTH_TOKEN`: a random value of at least 64 characters; it is used as
  the JWT signing key in the supplied Compose deployment.

Common optional values are `MELODEE_PORT`, `DB_MIN_POOL_SIZE`,
`DB_MAX_POOL_SIZE`, `JWT_ISSUER`, and `JWT_AUDIENCE`. See the
[Configuration Reference](/configuration-reference/) before adding application
settings to a Compose override.

## Native Development Run

Native development requires:

- .NET 10 SDK
- PostgreSQL 17 or a compatible PostgreSQL server
- `ffmpeg` on `PATH`
- Valid `DefaultConnection`, `MusicBrainzConnection`, and
  `ArtistSearchEngineConnection` connection strings

```bash
dotnet restore
dotnet run --project src/Melodee.Blazor
```

The development appsettings file contains machine-specific examples and is not
a production template. Supply your own secrets and paths through user secrets,
environment variables, or an untracked settings file.

Build or run the CLI with:

```bash
dotnet run --project src/Melodee.Cli -- --help
```

## First Start

1. Browse to `http://HOST:8080` or the configured port.
2. Register the first account; it becomes the initial administrator.
3. Complete onboarding and set the public `system.baseUrl`.
4. Verify all library paths under **Admin > Libraries**.
5. Run **Admin > Doctor**.
6. Configure metadata providers and job schedules as needed.

## Operations

```bash
# Service state
docker compose ps

# Application logs
docker compose logs -f melodee.blazor

# Health probe
curl --fail http://localhost:8080/health

# Stop without deleting volumes
docker compose down
```

Never use `docker compose down -v` during routine maintenance; `-v` deletes the
named volumes that hold the database and application data.

Continue with [Backup & Recovery](/backup/) before importing a large library,
and use the [Upgrade Guide](/upgrade/) for later releases.
