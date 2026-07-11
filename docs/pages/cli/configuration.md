---
title: CLI - Configuration Commands
description: List, read, set, and remove Melodee database-backed settings with mcli.
permalink: /cli/configuration/
tags:
  - cli
  - configuration
  - administration
---

# Configuration Commands

```bash
mcli configuration [--verbose] COMMAND
```

| Command | Usage |
|---------|-------|
| `list` | `mcli configuration list [--filter PATTERN] [--raw]` |
| `get` | `mcli configuration get KEY [--raw]` |
| `set` | `mcli configuration set [KEY] [VALUE] [--remove]` |

The `--filter` option supports wildcard patterns such as `imaging.*`. It is
named `--filter` (`-f`), not `--category`.

```bash
mcli configuration list --filter 'jobs.*'
mcli configuration get streaming.maxConcurrentStreams.perUser --raw
mcli configuration set system.baseUrl https://music.example.com
mcli configuration set obsolete.custom.setting --remove
```

Values are persisted as strings and interpreted by their consumers. Preserve
quotes around cron expressions, arrays, or values containing shell metacharacters.
An environment override takes precedence over the stored value.

Use `--remove` only for a custom or explicitly obsolete setting. Removing a
seeded setting may disable a feature or cause it to fall back unexpectedly.
Create a redacted [configuration export](/cli/backup/) before bulk changes.
