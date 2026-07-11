---
title: Quick Start
description: Run Melodee 2.2.0 with Docker or Podman and complete the first-time setup.
permalink: /quickstart/
tags:
  - installation
  - containers
  - homelab
---

# Quick Start

This guide gets a new Melodee 2.2.0 container installation running on port
`8080`. See [Installing](/installing/) for source builds, bind mounts, and native
development.

## Prerequisites

- A 64-bit Linux host using AMD64 or ARM64
- Docker with the Compose plugin, or Podman with a Compose provider
- At least 2 GB of RAM and 5 GB of free space before adding music
- Git, Python 3, and an available TCP port 8080

## Option 1: Use the Published Image

Clone the repository to obtain the supported Compose file and environment
template:

```bash
git clone https://github.com/melodee-project/melodee.git
cd melodee
cp example.env .env
```

Edit `.env` before starting. At minimum:

1. Replace `DB_PASSWORD` with a unique database password.
2. Replace `MELODEE_AUTH_TOKEN` with a random value of at least 64 characters.
3. Add this line to pin the application image:

   ```text
   MELODEE_IMAGE=ghcr.io/melodee-project/melodee:2.2.0
   ```

You can generate suitable secrets with OpenSSL:

```bash
openssl rand -base64 32
openssl rand -hex 32
```

Pull the application image and start both services without building locally:

```bash
docker compose pull melodee.blazor
docker compose up -d --no-build
docker compose ps
```

For Podman, use `podman compose` or `podman-compose` in place of
`docker compose`, depending on the installed provider.

## Option 2: Build Locally with the Setup Script

The repository includes a setup script that checks prerequisites, creates a
secure `.env`, builds the image, and can start the services:

```bash
git clone https://github.com/melodee-project/melodee.git
cd melodee
python3 scripts/run-container-setup.py --start
```

Use `--check-only` to run preflight checks without changing the installation.
The script prefers an installed Podman provider, then Docker.

## Complete Onboarding

1. Open `http://HOST:8080`, replacing `HOST` with the server name or address.
2. Register the first account. The first registered account becomes an
   administrator.
3. Follow the onboarding checklist and set `system.baseUrl` to the public URL
   clients will use, such as `https://music.example.com`.
4. Review **Admin > Libraries**. The default container paths are:

   | Library | Container path |
   |---------|----------------|
   | Inbound | `/app/inbound/` |
   | Staging | `/app/staging/` |
   | Storage | `/app/storage/` |
   | Podcasts | `/app/podcasts/` |
   | Templates | `/app/templates/` |
   | Themes | `/app/themes/` |

5. Open **Admin > Doctor** and resolve any failed checks before importing a
   large collection.

## Add Music

The default Compose file uses named volumes. For a one-time import, copy a
release directory into the inbound volume:

```bash
docker compose cp /path/to/release/. melodee.blazor:/app/inbound/release/
```

For an existing library or regular imports, configure host bind mounts instead;
see [Installing: Persistent storage](/installing/#persistent-storage). Do not
point the Inbound and Storage libraries at the same host directory.

Run the ingestion pipeline from **Admin > Jobs**, or from a locally configured
CLI:

```bash
./mcli library scan
```

Albums that need attention remain in Staging. Valid albums can move through
Storage and into the PostgreSQL index automatically.

## Verify the Installation

```bash
curl --fail http://localhost:8080/health
docker compose logs --tail=100 melodee.blazor
```

Melodee exposes one health endpoint: `/health`. A healthy container does not
guarantee every optional integration is configured, so also review
**Admin > Doctor**.

## Next Steps

- [Installation and storage options](/installing/)
- [Configuration](/configuration/)
- [Backup and recovery](/backup/)
- [Reverse proxy and homelab deployment](/homelab/)
- [Connect an OpenSubsonic client](/api-opensubsonic/)
