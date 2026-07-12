---
title: Configuration
description: Configure Melodee host settings, persistent application settings, libraries, providers, jobs, and security.
permalink: /configuration/
tags:
  - configuration
  - administration
  - security
---

# Configuration

Melodee has two configuration layers:

1. **Host configuration** controls startup concerns such as connection strings,
   JWT signing, CORS, rate limiting, and logging. It comes from
   `appsettings.json` and standard .NET environment variables.
2. **Application settings** control runtime behavior such as ingestion,
   providers, jobs, streaming, podcasts, Jukebox, themes, and scripting. They
   are stored in PostgreSQL and managed under **Admin > Settings**.

Environment variables take precedence in both layers. A setting supplied by an
environment variable appears read-only in normal administration workflows and
requires a container or service restart to change.

## First-Run Checklist

After registering the initial administrator:

1. Set `system.baseUrl` to the origin clients use, including `https://` and any
   non-default port.
2. Verify every path under **Admin > Libraries**.
3. Run **Admin > Doctor** and resolve failed required checks.
4. Review registration, download, streaming, and sharing settings before
   exposing the server outside your LAN.
5. Configure only the metadata providers you intend to use and supply their
   credentials.
6. Review job schedules before importing a large library.

## Libraries and Paths

The default container installation creates Inbound, Staging, Storage, User
Images, Playlist, Templates, Podcasts, and Themes libraries. The tracked
Compose file mounts their default `/app/...` paths.

You can override these library paths at startup by passing the following
variables into the application container:

| Variable | Library |
|----------|---------|
| `MELODEE_INBOUND_PATH` | Inbound |
| `MELODEE_STAGING_PATH` | Staging |
| `MELODEE_STORAGE_PATH` | First Storage library |
| `MELODEE_USER_IMAGES_PATH` | User Images |
| `MELODEE_PLAYLISTS_PATH` | Playlist Data |
| `MELODEE_TEMPLATES_PATH` | Templates |

The default `compose.yml` does not pass these optional variables. Add them to a
Compose override's `environment` section and ensure matching volume mounts
exist. Paths inside the container—not host paths—belong in these variables.

## Application Settings

Use **Admin > Settings** for interactive changes. The CLI provides an auditable
alternative:

```bash
# Find settings by wildcard
./mcli configuration list --filter 'streaming.*'

# Read one setting
./mcli configuration get system.baseUrl

# Change one setting
./mcli configuration set system.baseUrl https://music.example.com
```

The live Settings page and `mcli configuration list` are the authoritative list
for the installed version. Major categories include:

| Prefix | Purpose |
|--------|---------|
| `conversion.*`, `transcoding.*` | Ingestion conversion and stream output |
| `imaging.*` | Artwork sizes, limits, and embedded images |
| `jobs.*` | Quartz cron schedules |
| `jukebox.*`, `mpv.*`, `mpd.*` | Server-side playback |
| `magic.*`, `processing.*`, `validation.*` | Ingestion cleanup and validation |
| `podcast.*` | Feed security, download limits, quotas, and retention |
| `scrobbling.*` | Internal and Last.fm scrobbling |
| `scripting.*` | Event-script enablement and assignments |
| `searchEngine.*` | Metadata providers and local search databases |
| `security.*`, `register.*` | Secrets, password reset, registration, and blacklists |
| `streaming.*` | Response buffering and concurrency limits |
| `system.*` | Public URL, site name, downloads, and uploads |
| `theme.*` | Theme library behavior and validation |

Quote cron expressions and JSON-like values in the shell. Before bulk changes,
create a redacted export:

```bash
./mcli backup export --output melodee-settings.json --redact-secrets
```

## Environment Overrides for Application Settings

For database-backed application settings, Melodee maps underscores in an
environment-variable name to periods and compares keys without case
sensitivity. Examples:

```text
SYSTEM_BASEURL=https://music.example.com
STREAMING_MAXCONCURRENTSTREAMS_PERUSER=3
SEARCHENGINE_BRAVE_ENABLED=true
SEARCHENGINE_BRAVE_APIKEY=replace-with-secret
```

Compose only sends variables explicitly listed under a service's `environment`
or `env_file`. Values present in the host `.env` are available for Compose
substitution but are not automatically injected into the container.

Example override:

```yaml
services:
  melodee.blazor:
    environment:
      - SYSTEM_BASEURL=https://music.example.com
      - STREAMING_MAXCONCURRENTSTREAMS_PERUSER=3
      - SEARCHENGINE_BRAVE_ENABLED=true
      - SEARCHENGINE_BRAVE_APIKEY=${BRAVE_API_KEY}
```

Keep secrets out of tracked YAML. Use a protected `.env`, container secret
facility, or external secret manager appropriate to the deployment.

## Reverse Proxy

When TLS terminates at a reverse proxy:

- Set `system.baseUrl` to the external HTTPS origin.
- Preserve `Host`, `X-Forwarded-For`, and `X-Forwarded-Proto`.
- Support connection upgrades for Blazor Server and Party Mode.
- Disable response buffering for long audio streams if the proxy buffers by
  default.
- Restrict direct access to the backend port when trusting forwarded headers.

See [Homelab Deployment](/homelab/) for Nginx and Traefik examples.

## Apply and Validate Changes

Database-backed settings generally reset Melodee's configuration cache when
saved. Host settings and environment overrides require a restart:

```bash
docker compose restart melodee.blazor
docker compose logs --tail=100 melodee.blazor
curl --fail http://localhost:8080/health
```

Run **Admin > Doctor** after changing paths, connection strings, credentials, or
external tools. Test a representative browse and stream request after changing
authentication, proxy, streaming, or transcoding settings.

See the [Configuration Reference](/configuration-reference/) for startup keys
and defaults.
