---
title: CLI - Artist Commands
description: List, search, find duplicates, summarize, and delete artists with mcli.
permalink: /cli/artist/
tags:
  - cli
  - artists
  - administration
---

# Artist Commands

```bash
mcli artist [--verbose] COMMAND
```

| Command | Key arguments and options |
|---------|---------------------------|
| `list` | `--library`, `--limit` (50), `--raw` |
| `search [QUERY]` | `--since`, `--limit` (25), `--sort`, `--sort-dir`, `--raw`, `--delete`, `--keep-files`, `--yes` |
| `stats` | `--raw` |
| `find-duplicates` | `--artist-id`, `--source`, `--min-score` (0.7), `--include-low-confidence`, `--limit`, `--json`, `--merge` |
| `delete ID` | `--keep-files`, `--yes` |

```bash
mcli artist list --library Storage --limit 100
mcli artist search 'Miles Davis' --sort Albums --sort-dir desc
mcli artist find-duplicates --min-score 0.9 --json
mcli artist find-duplicates --source musicbrainz --limit 20
```

Run duplicate discovery without `--merge` first and inspect the suggested
primary artist. Merging, search deletion, and direct deletion can change or
remove records and files; create a backup before using them.
