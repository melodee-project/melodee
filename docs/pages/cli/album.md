---
title: CLI - Album Commands
description: List, search, inspect image issues, summarize, and delete albums with mcli.
permalink: /cli/album/
tags:
  - cli
  - albums
  - administration
---

# Album Commands

```bash
mcli album [--verbose] COMMAND
```

| Command | Key arguments and options |
|---------|---------------------------|
| `list` | `--limit` (50), `--status`, `--raw` |
| `search [QUERY]` | `--since`, `--limit` (25), `--sort`, `--sort-dir`, `--raw`, `--delete`, `--keep-files`, `--yes` |
| `stats` | `--raw` |
| `image-issues` | `--missing`, `--invalid`, `--misnumbered`, `--limit` (100), `--raw` |
| `delete ID` | `--keep-files`, `--yes` |

Valid `list --status` values are `Ok`, `New`, `NeedsAttention`, `Duplicate`, and
`Invalid`. Use `*` as the search query to match all albums; the query may be
omitted when `--since` is supplied.

```bash
mcli album list --status NeedsAttention --limit 100
mcli album search 'Abbey Road' --sort Year --sort-dir asc
mcli album search --since 7 --raw
mcli album image-issues --limit 50
```

`search --delete` and `delete` are destructive. Without `--keep-files`, deletion
can remove associated files as well as database records. Back up first and let
the confirmation prompt run before using `--yes`.
