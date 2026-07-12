---
title: CLI - User Commands
description: Create, list, inspect, and delete Melodee users with mcli.
permalink: /cli/user/
tags:
  - cli
  - users
  - administration
---

# User Commands

| Command | Purpose |
|---------|---------|
| `create` | Create a local user with `--username`, `--email`, and `--password` |
| `delete ID` | Delete a local user; `--yes` skips confirmation |
| `list` | List users locally or remotely; `--limit` defaults to 50 |
| `me` | Show the current user locally or remotely |

```bash
mcli user create --username alice --email alice@example.com --password 'replace-me'
mcli user list --limit 25
mcli user me
mcli user delete 42
```

`create --force` deletes and recreates an existing matching account. This can
remove related account state, so prefer correcting the existing user in the
administration UI.

Only `list` and `me` support remote mode. Remote `list` requires an administrator
JWT; remote `me` requires an authenticated user's JWT:

```bash
mcli user me --server https://music.example.com --token 'eyJ...'
mcli user list --server https://music.example.com --token 'eyJ...' --limit 25
```

See [CLI Remote Server Mode](/cli-remote-mode/) for profiles, environment
variables, and exit codes.
