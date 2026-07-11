---
title: CLI - Job Commands
description: List and run Melodee background jobs synchronously with mcli.
permalink: /cli/job/
tags:
  - cli
  - jobs
  - administration
---

# Job Commands

The `job` group runs background work synchronously in the current CLI process.
It is local-only and uses the same databases, paths, and settings as the server.

| Command | Purpose |
|---------|---------|
| `job list [--raw]` | Show known jobs, execution history, and statistics |
| `job run --job NAME` | Run a registered job by class name |
| `job artistsearchengine-refresh` | Refresh the artist search database |
| `job musicbrainz-update` | Download MusicBrainz data and rebuild its local database |

```bash
mcli job list
mcli job run --job ChartUpdateJob
mcli job --batchsize 500 artistsearchengine-refresh
mcli job musicbrainz-update
```

`--batchsize` (`-b`) and `--verbose` are options on the `job` command and must
appear before its subcommand. The batch-size override applies only to commands
that consume it.

An ad hoc run can overlap a Quartz-scheduled run. Check the Jobs administration
page before starting resource-intensive work, and see [Background Jobs](/jobs/)
for configured schedules and dependencies.
