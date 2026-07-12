---
title: CLI - Import Commands
description: Import a user's favorite songs from a CSV file with mcli.
permalink: /cli/import/
tags:
  - cli
  - import
  - users
---

# Import Commands

`import user-favorite-songs` maps CSV columns to artists, albums, and songs,
then adds matching songs to a user's favorites.

```bash
mcli import favorites.csv user-favorite-songs \
  USER_API_KEY ArtistColumn AlbumColumn SongColumn --pretend
```

The positional values are:

1. CSV filename
2. User API key (GUID)
3. Artist-name column
4. Album-name column
5. Song-name column

Run with `--pretend` first. It performs matching and reports what would happen
without changing favorites. Column names must match the CSV header, and the CLI
must have local access to PostgreSQL and the CSV file.
