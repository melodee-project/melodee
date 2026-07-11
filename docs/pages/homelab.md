---
title: Homelab Deployment
description: Deploy Melodee safely behind a reverse proxy with persistent storage and routine monitoring.
permalink: /homelab/
tags:
  - deployment
  - homelab
  - reverse-proxy
  - containers
---

# Homelab Deployment

This guide builds on the [Quick Start](/quickstart/) and covers the choices that
matter for a long-running home server: storage, networking, TLS, permissions,
and monitoring.

## Recommended Layout

Use separate storage for the PostgreSQL database, working directories, and
published media when possible:

```text
Fast local disk
├── PostgreSQL volume
├── inbound
├── staging
└── logs

Capacity storage or NAS
├── storage
├── podcasts
├── playlists
├── themes
└── templates
```

The default Compose deployment uses named volumes. A
`compose.override.yml` can bind existing host directories to the container:

```yaml
services:
  melodee.blazor:
    volumes:
      - /srv/melodee/inbound:/app/inbound
      - /srv/melodee/staging:/app/staging
      - /mnt/music:/app/storage
      - /srv/melodee/podcasts:/app/podcasts
```

Avoid mounting Inbound, Staging, and Storage on the same directory. Melodee
moves and rewrites files while processing, so overlapping roots can cause
reprocessing or data loss.

For NFS or SMB storage, mount the share on the host first and bind that host
mount into the container. Store SMB credentials in a root-readable credentials
file rather than placing a password directly in shell history or Compose YAML.

## Network Exposure

The supplied Compose file publishes `MELODEE_PORT` (default `8080`) on all host
interfaces. For a LAN-only installation, restrict access with the host firewall.
For internet access, place Melodee behind a TLS-terminating reverse proxy and do
not expose PostgreSQL.

After configuring the proxy, set `system.baseUrl` under **Admin > Settings** to
the external origin, for example `https://music.example.com`. Do not include a
trailing API path.

### Nginx Example

Blazor Server and Party Mode use upgraded connections, while audio streaming
benefits from disabled proxy buffering:

```nginx
server {
    listen 443 ssl;
    server_name music.example.com;

    ssl_certificate /etc/letsencrypt/live/music.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/music.example.com/privkey.pem;

    client_max_body_size 100m;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_buffering off;
        proxy_read_timeout 3600s;
    }
}
```

Melodee enables forwarded-header processing by default. Keep the application
reachable only through a trusted proxy when relying on forwarded client
addresses.

### Traefik Labels

```yaml
services:
  melodee.blazor:
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.melodee.rule=Host(`music.example.com`)"
      - "traefik.http.routers.melodee.entrypoints=websecure"
      - "traefik.http.routers.melodee.tls=true"
      - "traefik.http.services.melodee.loadbalancer.server.port=8080"
```

## Container Resources

The supplied Compose file caps the application at one CPU and 2 GB of memory.
That is adequate for initial evaluation but can constrain large scans,
transcoding, or multiple streams. Adjust the `deploy.resources.limits` values in
an override after observing the host; do not remove limits on a shared machine
without monitoring.

Keep the PostgreSQL volume on low-latency storage. Media can live on slower or
network storage, but scans and imports will be limited by that storage's random
I/O and metadata performance.

See [Hardware & Performance](/hardware/) for starting points.

## Rootless Podman and Permissions

The setup helper includes a permissions diagnostic:

```bash
python3 scripts/run-container-setup.py --check-permissions
```

For bind mounts, ensure the mapped container process can traverse every parent
directory and write the Inbound, Staging, Storage, podcast, theme, and template
paths. Prefer narrowly scoped ownership or ACLs over world-writable modes.

## Audio Output and Jukebox

The published image includes `ffmpeg`, but it does not install MPV or MPD or
automatically expose host audio devices. Server-side [Jukebox](/jukebox/)
therefore requires a custom image or a separately reachable MPD instance, plus
explicit audio-device access. Ordinary browser and API streaming does not need
host audio hardware.

## Monitoring

```bash
docker compose ps
docker compose logs --tail=200 melodee.blazor
docker compose logs --tail=100 melodee-db
docker stats
curl --fail http://localhost:8080/health
```

The `/health` endpoint is the container liveness/readiness probe. Use
**Admin > Doctor** for deeper checks such as library paths, required tools,
DecentDB compatibility, search-provider credentials, and writable storage.

Monitor at least:

- Free space on the database, Inbound, Staging, Storage, and backup filesystems
- Application restarts and unhealthy container states
- PostgreSQL backup completion and restore tests
- Scan duration, transcoding CPU use, and application log growth

## Routine Operations

- Back up PostgreSQL and persistent application volumes before upgrades.
- Pin release images instead of relying on `latest` for unattended updates.
- Review release notes before changing versions.
- Keep the proxy, container runtime, host kernel, and PostgreSQL image patched.
- Test restores on a separate host or isolated Compose project.

See [Backup & Recovery](/backup/) and [Upgrade Guide](/upgrade/) for executable
procedures.
