---
title: CLI Remote Server Mode
description: Use mcli's supported remote commands with a Melodee JWT bearer token.
permalink: /cli-remote-mode/
tags:
  - cli
  - remote
  - api
  - security
---

# CLI Remote Server Mode

Remote mode calls the native REST API over HTTP or HTTPS. In Melodee 2.2.0 it
supports exactly four operations:

| Command | Required capability |
|---------|---------------------|
| `mcli system ... info` | None for server info; a token is still required by `mcli` |
| `mcli user me ...` | Authenticated user |
| `mcli user list ...` | Administrator |
| `mcli search ...` | Authenticated user |

Library processing, jobs, configuration, backups, and destructive data commands
are local-only.

## Obtain a JWT

Authenticate against the native API. Avoid placing the password in shell
history; the example is suitable for an interactive test after substituting
safe input handling:

```bash
curl --fail --silent --show-error \
  --request POST https://music.example.com/api/v1/auth/authenticate \
  --header 'Content-Type: application/json' \
  --data '{"userName":"alice","email":null,"password":"replace-me"}'
```

The response's `token` is a JWT access token. Its default lifetime is 15
minutes. A user's persistent GUID API key is not interchangeable with this JWT.

Set the token in the environment rather than a command-line option:

```bash
export MELODEE_SERVER=https://music.example.com
export MELODEE_TOKEN='eyJ...'
```

## Commands

```bash
mcli user me
mcli user list --limit 25
mcli search 'Miles Davis' --limit 10
mcli system info
```

When using flags instead of environment variables, option placement follows
the command settings:

```bash
mcli user me --server https://music.example.com --token 'eyJ...'
mcli user list --server https://music.example.com --token 'eyJ...'
mcli search 'Miles Davis' --server https://music.example.com --token 'eyJ...'
mcli system --server https://music.example.com --token 'eyJ...' info
```

`--server` is not a top-level option, so placing it before `search`, `system`,
or `user` produces a usage error.

## Profiles

The CLI loads profiles from:

- Linux/macOS: `${XDG_CONFIG_HOME:-$HOME/.config}/melodee/mcli.json`
- Windows: `%APPDATA%\melodee\mcli.json`

```json
{
  "profiles": {
    "home": {
      "server": "https://music.example.com",
      "token": "eyJ..."
    }
  },
  "defaults": {
    "profile": "home"
  }
}
```

Use a named profile with the supported command:

```bash
mcli user me --profile home
mcli system --profile home info
```

Profiles store tokens as clear text. Restrict the file to the owning user and
remember that short-lived JWTs expire:

```bash
chmod 600 ~/.config/melodee/mcli.json
```

## Precedence

Remote settings resolve from highest to lowest priority:

1. `--server`, `--token`, and `--profile` on the applicable command
2. `MELODEE_SERVER`, `MELODEE_TOKEN`, and `MELODEE_PROFILE`
3. The selected profile, including `defaults.profile`

Remote mode activates only when a server URL resolves. A server without a token
returns a configuration error.

## JSON Output

The four remote-capable command settings expose `--json`. It selects compact
JSON instead of indented JSON where supported:

```bash
mcli user me --json
mcli search 'Miles Davis' --limit 5 --json
mcli system --json info
```

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `2` | Missing remote configuration or token |
| `10` | DNS, connection, or TLS failure |
| `11` | Timeout |
| `12` | HTTP 401 or 403 |
| `13` | HTTP 404 |
| `14` | HTTP 5xx |
| `15` | Unexpected response or serialization failure |

## Security

- Prefer HTTPS except on an isolated loopback connection.
- Do not pass tokens through shared process listings or shell history.
- Unset `MELODEE_TOKEN` after use on shared systems.
- Do not commit `mcli.json`.
- Use an administrator token only for `user list`; use a least-privileged user
  for search and self-service commands.

See [Native API authentication](/api/#authentication) and the
[CLI command reference](/cli/).
