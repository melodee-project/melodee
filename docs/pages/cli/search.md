---
title: CLI - Search Command
description: Search Melodee artists, albums, songs, and playlists locally or remotely with mcli.
permalink: /cli/search/
tags:
  - cli
  - search
  - api
---

# Search Command

```bash
mcli search QUERY [--limit 25] [--verbose]
```

Local mode searches using the configured databases:

```bash
mcli search 'Miles Davis' --limit 10
```

When a remote server resolves from `--server`, environment variables, or a
profile, the command calls the native REST API:

```bash
MELODEE_SERVER=https://music.example.com \
MELODEE_TOKEN='eyJ...' \
mcli search 'Miles Davis' --limit 10 --json
```

Results can include artists, albums, songs, and playlists. `--json` is a remote
mode option and selects compact JSON; local presentation is controlled by the
local command implementation. See [CLI Remote Server Mode](/cli-remote-mode/)
for authentication and precedence.
