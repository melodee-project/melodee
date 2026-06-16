---
title: CLI - Doctor Command
permalink: /cli/doctor/
layout: page
---

# Doctor Command

`mcli doctor` runs a set of diagnostics to confirm the CLI can load configuration, connect to databases, and access library paths.

## Usage

```bash
mcli doctor [OPTIONS]
```

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--raw` | `false` | Output structured JSON instead of formatted tables |
| `--verbose` | `false` | Include extra diagnostic details |
| `--write-test` | `false` | Create+delete a temp file in each library directory to validate write access |

## What It Checks

1. **Configuration**
   - Confirms configuration is loadable and shows where it came from (`MELODEE_APPSETTINGS_PATH` or local `appsettings*.json`)
   - Validates required connection strings exist
2. **Database connectivity**
   - Postgres (`DefaultConnection`)
   - MusicBrainz DecentDB (`MusicBrainzConnection`)
   - ArtistSearchEngine DecentDB (`ArtistSearchEngineConnection`)
3. **Library paths**
   - Ensures each configured library path exists
   - Optionally validates write access with `--write-test`

## Examples

### Basic health check

```bash
./mcli doctor
```

### Validate write permissions (recommended for deployments)

```bash
./mcli doctor --write-test
```

### JSON output for automation

```bash
./mcli doctor --raw | jq '.checks[] | select(.success==false)'
```

## Example Output

```
✓ Configuration
✓ Database: Postgres
✓ Database: MusicBrainz (DecentDB)
✓ Database: ArtistSearchEngine (DecentDB)
✓ Libraries

All checks passed.
```

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | All checks passed |
| `1` | One or more checks failed |
