---
title: Configuration Reference
description: Reference for Melodee 2.2.0 Compose variables, .NET host settings, and database-backed settings.
permalink: /configuration-reference/
tags:
  - configuration
  - environment-variables
  - reference
---

# Configuration Reference

This reference separates variables interpreted by the supplied Compose file
from settings read by the Melodee process. Use `__` (double underscore) in
environment variables for nested .NET host configuration.

## Compose Variables

These variables are consumed by `compose.yml` on the host:

| Variable | Default | Purpose |
|----------|---------|---------|
| `DB_PASSWORD` | No safe default | PostgreSQL password and application connection-string value |
| `DB_MIN_POOL_SIZE` | `10` | Minimum Npgsql pool size in the generated connection string |
| `DB_MAX_POOL_SIZE` | `50` | Maximum Npgsql pool size in the generated connection string |
| `MELODEE_PORT` | `8080` | Published host port mapped to container port 8080 |
| `MELODEE_IMAGE` | `localhost/melodee:latest` | Application image name or release tag |
| `DOCKERFILE_PATH` | `Dockerfile` | Dockerfile used by local builds |
| `MELODEE_AUTH_TOKEN` | Insecure placeholder | JWT signing key and legacy Melodee token in the supplied deployment |
| `MELODEE_AUTH_TOKEN_HOURS` | `24` | Legacy token lifetime fallback |
| `JWT_ISSUER` | `MelodeeApi` | Expected JWT issuer |
| `JWT_AUDIENCE` | `MelodeeClient` | Expected JWT audience |

Replace both secret placeholders before the first start. A signing key should
be random and at least 64 characters in the supplied deployment.

## Connection Strings

| Environment variable | Required | Purpose |
|----------------------|----------|---------|
| `ConnectionStrings__DefaultConnection` | Yes | Primary PostgreSQL database |
| `ConnectionStrings__MusicBrainzConnection` | Yes | Local MusicBrainz DecentDB file |
| `ConnectionStrings__ArtistSearchEngineConnection` | Yes | Local artist-search DecentDB file |

The tracked Compose file supplies all three. For native runs, provide them in an
untracked settings file or environment. DecentDB connection strings use a
`Data Source=...` path; see [DecentDB Usage & Migration](/decentdb/).

## JWT and Authentication

| Key / environment variable | Default | Notes |
|----------------------------|---------|-------|
| `Jwt:Key` / `Jwt__Key` | None | Required signing secret |
| `Jwt:Issuer` / `Jwt__Issuer` | `MelodeeApi` | Must match issued tokens |
| `Jwt:Audience` / `Jwt__Audience` | `MelodeeClient` | Must match issued tokens |
| `Auth:SelfRegistrationEnabled` / `Auth__SelfRegistrationEnabled` | `true` | Allows account registration subject to Melodee registration settings |
| `Auth:Google:Enabled` | `false` | Enables Google ID-token authentication |
| `Auth:Google:ClientId` | Empty | Primary Google OAuth client ID |
| `Auth:Google:AdditionalClientIds` | Empty | Additional accepted client IDs |
| `Auth:Google:AllowedHostedDomains` | Empty | Optional hosted-domain allowlist |
| `Auth:Google:AutoLinkEnabled` | `false` | Allows configured account linking behavior |
| `Auth:Google:ClockSkewSeconds` | `300` | Token validation clock skew |
| `Auth:Tokens:AccessTokenLifetimeMinutes` | `15` | JWT access-token lifetime |
| `Auth:Tokens:RefreshTokenLifetimeDays` | `30` | Refresh-token lifetime |
| `Auth:Tokens:MaxSessionDays` | `90` | Maximum session age |
| `Auth:Tokens:RotateRefreshTokens` | `true` | Rotates tokens at refresh |
| `Auth:Tokens:RevokeOnReplay` | `true` | Revokes a token family after replay detection |

Arrays use numeric environment suffixes, for example:

```text
Auth__Google__AllowedHostedDomains__0=example.com
Auth__Google__AdditionalClientIds__0=client-id.apps.googleusercontent.com
```

Changing the JWT key invalidates existing access tokens. Preserve the key in
backups and rotate it as a planned security operation.

## CORS and Forwarded Headers

| Key | Default | Notes |
|-----|---------|-------|
| `Cors:AllowedOrigins` | Local development origins | Explicit browser origins; production uses an empty allowlist if none are configured |
| `UseForwardedHeaders` | `true` | Processes proxy forwarding headers |

Example production origin:

```text
Cors__AllowedOrigins__0=https://music.example.com
```

Origins are scheme, host, and optional port—not URL paths. Do not use `*` with
credentialed browser requests.

## Rate Limiting

Native API defaults:

| Key | Default |
|-----|---------|
| `RateLimiting:MelodeeApi:TokenLimit` | `30` |
| `RateLimiting:MelodeeApi:QueueLimit` | `10` |
| `RateLimiting:MelodeeApi:ReplenishmentPeriodSeconds` | `30` |
| `RateLimiting:MelodeeApi:TokensPerPeriod` | `30` |
| `RateLimiting:MelodeeApi:AutoReplenishment` | `true` |
| `RateLimiting:MelodeeAuth:TokenLimit` | `10` |
| `RateLimiting:MelodeeAuth:QueueLimit` | `5` |
| `RateLimiting:MelodeeAuth:ReplenishmentPeriodSeconds` | `60` |
| `RateLimiting:MelodeeAuth:TokensPerPeriod` | `10` |
| `RateLimiting:MelodeeAuth:AutoReplenishment` | `true` |

Authentication limits must remain at least as strict as general API limits;
startup validation rejects inconsistent values.

Jellyfin compatibility defaults:

| Key | Default |
|-----|---------|
| `Jellyfin:RateLimit:ApiTokenLimit` | `200` |
| `Jellyfin:RateLimit:ApiPeriodSeconds` | `60` |
| `Jellyfin:RateLimit:AuthTokenLimit` | `10` |
| `Jellyfin:RateLimit:AuthPeriodSeconds` | `60` |
| `Jellyfin:RateLimit:StreamConcurrentLimit` | `10` |

## Custom Blocks

| Key | Default | Description |
|-----|---------|-------------|
| `CustomBlocks:Enabled` | `true` | Loads configured Markdown blocks |
| `CustomBlocks:MaxBytes` | `262144` | Maximum source file size (256 KiB) |
| `CustomBlocks:CacheSeconds` | `30` | In-memory cache duration; `0` disables caching |

See [Custom Blocks](/custom-blocks/) for valid slots and the HTML sanitizer
allowlist.

## Scheduler, Logging, and Host Settings

| Key | Default | Description |
|-----|---------|-------------|
| `QuartzDisabled` | `false` | Disables all Quartz scheduling when `true` |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Selects environment-specific appsettings |
| `AllowedHosts` | `*` | ASP.NET Core host filtering |
| `Serilog:*` | See `appsettings.json` | Console and rolling compact-JSON file logging |

Scheduled jobs are individually controlled by database-backed `jobs.*`
settings. Clearing a job's cron expression prevents it from being scheduled.

## Library-Path Startup Overrides

The application recognizes these optional variables when they are explicitly
passed to the process:

| Variable | Default container library path |
|----------|--------------------------------|
| `MELODEE_STORAGE_PATH` | `/app/storage/` |
| `MELODEE_INBOUND_PATH` | `/app/inbound/` |
| `MELODEE_STAGING_PATH` | `/app/staging/` |
| `MELODEE_USER_IMAGES_PATH` | `/app/user-images/` |
| `MELODEE_PLAYLISTS_PATH` | `/app/playlists/` |
| `MELODEE_TEMPLATES_PATH` | `/app/templates/` |

The supplied Compose file mounts these default paths but does not inject the
optional override variables.

## Database-Backed Application Settings

The complete list evolves with the schema and is available from the running
version:

```bash
./mcli configuration list
./mcli configuration list --filter 'podcast.*'
./mcli configuration get streaming.maxConcurrentStreams.perUser
```

To override a database setting with an environment variable, replace periods
with underscores. Matching is case-insensitive:

```text
system.baseUrl                           -> SYSTEM_BASEURL
streaming.useBufferedResponses           -> STREAMING_USEBUFFEREDRESPONSES
streaming.maxConcurrentStreams.global    -> STREAMING_MAXCONCURRENTSTREAMS_GLOBAL
podcast.http.allowHttp                   -> PODCAST_HTTP_ALLOWHTTP
searchEngine.spotify.apiKey              -> SEARCHENGINE_SPOTIFY_APIKEY
```

Relevant setting groups and their dedicated guides:

- `jobs.*`: [Background Jobs](/jobs/)
- `jukebox.*`, `mpv.*`, `mpd.*`: [Jukebox](/jukebox/)
- `podcast.*`: [Podcasts](/podcasts/)
- `scripting.*`: [Event Scripting](/scripting/)
- `theme.*`, `system.defaultTheme`: [Theming](/theming/)
- `userDeviceProfile.enabled`: [User Device Profiles](/user-device-profiles/)

## CLI-Specific Variables

| Variable | Purpose |
|----------|---------|
| `MELODEE_APPSETTINGS_PATH` | Local-mode CLI settings file |
| `MELODEE_ENVIRONMENT` | CLI environment fallback |
| `MELODEE_SERVER` | Remote-mode server origin |
| `MELODEE_TOKEN` | Remote-mode JWT bearer token |
| `MELODEE_PROFILE` | Remote-mode profile name |

See [CLI](/cli/) and [CLI Remote Server Mode](/cli-remote-mode/) before storing
tokens in profiles or automation.
