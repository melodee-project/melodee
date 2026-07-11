---
title: Command Line Interface (CLI)
description: Run Melodee administration, library, diagnostics, search, and maintenance commands with mcli.
permalink: /cli/
tags:
  - cli
  - administration
  - automation
---

# Command Line Interface (CLI)

`mcli` provides local database/filesystem administration and a small remote API
mode. The command's built-in help is the source of truth for the installed
version:

```bash
mcli --help
mcli help library
mcli help library scan
```

## Run the CLI

The published application image includes the CLI at `/app/cli/mcli`:

```bash
docker compose exec melodee.blazor /app/cli/mcli --help
docker compose exec melodee.blazor /app/cli/mcli doctor
```

This is the simplest local mode because the container already has the same
connection strings and volume mounts as the server.

To build it from source:

```bash
dotnet build src/Melodee.Cli/Melodee.Cli.csproj
src/Melodee.Cli/bin/Debug/net10.0/mcli --help
```

## Local Configuration

Local mode opens PostgreSQL, DecentDB, and library paths directly. Run it on a
trusted machine with access to those resources.

| Variable | Purpose |
|----------|---------|
| `MELODEE_APPSETTINGS_PATH` | Path to a specific `appsettings.json` file |
| `ASPNETCORE_ENVIRONMENT` | Selects `appsettings.{Environment}.json` |
| `MELODEE_ENVIRONMENT` | CLI environment fallback |
| `ConnectionStrings__DefaultConnection` | PostgreSQL override |
| `ConnectionStrings__MusicBrainzConnection` | MusicBrainz DecentDB override |
| `ConnectionStrings__ArtistSearchEngineConnection` | Artist Search DecentDB override |

```bash
MELODEE_APPSETTINGS_PATH=/etc/melodee/appsettings.json mcli library list
```

## Command Reference

| Command | Purpose | Reference |
|---------|---------|-----------|
| `album` | List, search, inspect, and delete albums | [Album](/cli/album/) |
| `artist` | List, search, deduplicate, and delete artists | [Artist](/cli/artist/) |
| `backup export` | Export settings and libraries | [Backup](/cli/backup/) |
| `configuration` | Read and change database-backed settings | [Configuration](/cli/configuration/) |
| `doctor` | Diagnose the installation | [Doctor](/cli/doctor/) |
| `file mpeg` | Inspect an audio file | [File](/cli/file/) |
| `import` | Import user favorite songs from CSV | [Import](/cli/import/) |
| `job` | List and run background jobs | [Job](/cli/job/) |
| `library` | Process, move, scan, validate, and report on libraries | [Library](/cli/library/) |
| `parser parse` | Parse CUE, M3U, NFO, or SFV metadata | [Parser](/cli/parser/) |
| `search` | Search artists, albums, songs, and playlists | [Search](/cli/search/) |
| `system info` | Print server information | [System](/cli/system/) |
| `tags show` | Inspect tags in a media file | [Tags](/cli/tags/) |
| `user` | Create, list, inspect, and delete users | [User](/cli/user/) |
| `validate` | Validate `melodee.json` album metadata | [Validate](/cli/validate/) |

## Common Examples

```bash
# Deep installation checks
mcli doctor --write-test

# Full Inbound -> Staging -> Storage -> PostgreSQL workflow
mcli library scan

# JSON scan summary without progress rendering
mcli library scan --json

# Search recent albums
mcli album search --since 7 --sort Added --sort-dir desc

# Create a user
mcli user create --username alice --email alice@example.com --password 'replace-me'

# Export configuration without clear-text secrets
mcli backup export --output melodee-settings.json --redact-secrets
```

## Output and Exit Codes

Output flags are command-specific; `--json`, `--raw`, `--silent`, and
`--verbose` are not universal global options. Check the target command's help
before using it in automation.

Most commands use `0` for success and a nonzero value for failure, but some
legacy commands have command-specific behavior. Remote mode reserves exit codes
10-15; see [CLI Remote Server Mode](/cli-remote-mode/#exit-codes). Test the exact
command and version before making an automation decision solely from its exit
status.

## Destructive Commands

Commands such as `album delete`, `artist delete`, `library clean`,
`library purge`, duplicate merging, and `configuration set --remove` can remove
database records or files. Create a [backup](/backup/), omit confirmation-bypass
flags on the first run, and use preview or JSON modes where provided.

## Remote Mode

Only `search`, `system info`, `user me`, and `user list` currently use the
remote REST client. Other commands remain local even if similarly named. See
[CLI Remote Server Mode](/cli-remote-mode/) for token acquisition, exact option
placement, and security guidance.
