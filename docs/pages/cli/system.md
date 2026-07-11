---
title: CLI - System Command
description: Display Melodee server identity and version information locally or remotely with mcli.
permalink: /cli/system/
tags:
  - cli
  - diagnostics
  - api
---

# System Command

`system info` reports the server name, description, and version.

```bash
mcli system info
```

Remote connection options belong to the `system` group, before `info`:

```bash
mcli system --server https://music.example.com --token 'eyJ...' info
mcli system --profile home --json info
```

The CLI currently requires a token whenever remote mode is active, even though
the underlying server-information endpoint is public. See [CLI Remote Server
Mode](/cli-remote-mode/) for token acquisition and secure storage guidance.
