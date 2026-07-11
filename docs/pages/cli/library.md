---
title: CLI - Library Commands
description: Process, scan, inspect, repair, and purge Melodee libraries with mcli.
permalink: /cli/library/
tags:
  - cli
  - libraries
  - administration
---

# Library Commands

Library commands operate directly on local databases and mounted media paths.
Use the same effective user and mounts as the server.

| Command | Important options |
|---------|-------------------|
| `album-report` | `--library`, `--full`, `--raw` |
| `clean` | `--library` |
| `find-duplicate-dirs` | `--library`, `--artist`, `--limit`, `--search`, `--merge`, `--json` |
| `list` | `--library`, `--raw` |
| `move-ok` | `--library`, `--to-library`, or path mode with `--from-path` and `--to-path` |
| `process` | `--library`, `--copy`, `--force`, `--limit`, `--pre-script`, or path mode with `--inbound` and `--staging` |
| `purge` | `--library` |
| `rebuild [only-path]` | `--library`, `--only-missing`, `--skip-images`, `--limit` |
| `scan` | `--force`, `--json`, `--silent`, `--verbose` |
| `stats` | `--library`, `--borked`, `--raw` |
| `validate` | `--library`, `--fix`, `--json` |

## Common Workflows

Run the complete Inbound to Staging to Storage to PostgreSQL workflow:

```bash
mcli library scan
mcli library scan --json
```

Inspect integrity before allowing removal of orphaned database records:

```bash
mcli library validate --library Storage --json
mcli library validate --library Storage --fix
```

Review duplicate directories before merging them:

```bash
mcli library find-duplicate-dirs --library Storage --search --json
mcli library find-duplicate-dirs --library Storage --search --merge
```

`--merge` requires `--search`. The older `--delete` option is deprecated and is
now an alias for merge behavior.

## Destructive Operations

`clean` deletes directories without media files. `purge` deletes a library's
artists, albums, and songs and resets its statistics. `validate --fix`, moving,
processing with move semantics, and duplicate merging also change persistent
state. Back up PostgreSQL and media first, target a library explicitly, and run
the available report or JSON mode before making changes.

See [Libraries](/libraries/) for the directory workflow and [Backup and
Restore](/backup/) for a complete recovery set.
