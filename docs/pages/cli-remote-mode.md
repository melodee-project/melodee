---
title: CLI Remote Server Mode
description: Use mcli to manage remote Melodee servers via REST API
tags:
  - cli
  - remote
  - api
---

# CLI Remote Server Mode

## Overview

`mcli` supports **Remote Server Mode**, allowing you to manage Melodee servers remotely over HTTPS using the REST API. This enables administration from any workstation without needing access to the server filesystem.

## Quick Start

### Basic Remote Command

```bash
mcli --server https://demo.melodee.org --token YOUR_API_TOKEN system info
```

### Using Environment Variables

```bash
export MELODEE_SERVER=https://demo.melodee.org
export MELODEE_TOKEN=YOUR_API_TOKEN

mcli system info
mcli user me
mcli user list
```

### Using Configuration Profiles

Create a config file at:
- Linux/macOS: `~/.config/melodee/mcli.json`
- Windows: `%APPDATA%\melodee\mcli.json`

```json
{
  "profiles": {
    "demo": {
      "server": "https://demo.melodee.org",
      "token": "your-api-token-here"
    },
    "prod": {
      "server": "https://melodee.example.com",
      "token": "your-prod-token-here"
    }
  },
  "defaults": {
    "profile": "demo"
  }
}
```

Then use the profile:

```bash
mcli --profile prod system info
mcli --profile demo user me
```

## Global Options

| Option | Environment Variable | Description |
|--------|---------------------|-------------|
| `--server <URL>` | `MELODEE_SERVER` | Remote server URL (e.g., `https://demo.melodee.org`) |
| `--token <TOKEN>` | `MELODEE_TOKEN` | API authentication token (Bearer token) |
| `--profile <NAME>` | `MELODEE_PROFILE` | Profile name from config file |
| `--json` | N/A | Output compact JSON (default: pretty-printed) |

## Precedence Rules

Options are resolved in this order (highest priority first):

1. **Command line flags** (`--server`, `--token`, `--profile`)
2. **Environment variables** (`MELODEE_SERVER`, `MELODEE_TOKEN`, `MELODEE_PROFILE`)
3. **Config file profile** (from `defaults.profile` or explicit `--profile`)

## Remote Mode Commands

The following commands work in both local and remote mode:

### System Information

```bash
mcli system info
```

Returns server version, name, and description.

### Current User

```bash
mcli user me
```

Returns information about the authenticated user.

### List Users (Admin Only)

```bash
mcli user list
```

Lists all users (requires admin privileges).

### Search

```bash
mcli search "Pink Floyd"
mcli search "Dark Side" --limit 10
```

Search for artists, albums, songs, and playlists.

## Output Formats

### Pretty JSON (Default)

```bash
mcli system info
```

Outputs formatted JSON with indentation.

### Compact JSON

```bash
mcli --json system info
```

Outputs compact JSON on a single line.

## Security

### Token Safety

**⚠️ SECURITY WARNING**: Never pass tokens on the command line in production scripts or shared environments. Tokens passed via `--token` are visible in shell history.

**Recommended approaches** (in order of preference):

1. **Use config file profiles** - Most secure for repeated use
2. **Use environment variables** - Good for CI/CD and scripts
3. **Use command line flags** - Only for one-off manual commands

### Token Storage

The config file (`mcli.json`) stores tokens in plain text. Ensure proper file permissions:

```bash
# Linux/macOS
chmod 600 ~/.config/melodee/mcli.json
```

## Error Codes

`mcli` uses deterministic exit codes for remote mode:

| Exit Code | Meaning |
|-----------|---------|
| 0 | Success |
| 2 | Usage/config error (missing server/token/profile) |
| 10 | Network error (DNS, connection refused, TLS handshake) |
| 11 | Timeout |
| 12 | Unauthorized/Forbidden (HTTP 401/403) |
| 13 | Not found (HTTP 404) |
| 14 | Server error (HTTP 5xx) |
| 15 | Unexpected/serialization error |

## Examples

### Get System Info from Demo Server

```bash
mcli --server https://demo.melodee.org --token demo-token system info
```

### List Users with Profile

```bash
# First, create profile in ~/.config/melodee/mcli.json
mcli --profile demo user list
```

### Search from Remote Server

```bash
export MELODEE_SERVER=https://demo.melodee.org
export MELODEE_TOKEN=your-token

mcli search "Miles Davis" --limit 5 --json
```

### Admin Operations

```bash
# Requires admin token
mcli --server https://melodee.example.com --token admin-token user list
```

## Troubleshooting

### "ERROR: Missing API token"

Ensure you provide a token via `--token`, `MELODEE_TOKEN`, or a config profile.

### "ERROR (401 Unauthorized): API token invalid or expired"

Your token is invalid or has expired. Obtain a new token from the Melodee web interface.

### "ERROR (403 Forbidden): Token does not have permission"

Your token doesn't have the required permissions (e.g., admin access for `user list`).

### "ERROR (404 Not Found): Endpoint not available"

The server version doesn't support the requested endpoint. Ensure server and client versions are compatible.

## Local Mode vs Remote Mode

| Feature | Local Mode | Remote Mode |
|---------|-----------|-------------|
| **Trigger** | No `--server` flag | `--server` flag provided |
| **Access** | Direct database/filesystem access | REST API over HTTPS |
| **Authentication** | Not required | API token required |
| **Use Case** | Local administration | Remote administration |
| **Speed** | Faster (direct access) | Slower (network latency) |

## Getting an API Token

1. Log in to the Melodee web interface
2. Navigate to **User Settings → API Tokens**
3. Click **Generate New Token**
4. Copy the token and store it securely

## Best Practices

1. **Use profiles for frequent remote access** - Avoid typing server/token repeatedly
2. **Set default profile** - Configure `defaults.profile` in config file
3. **Rotate tokens regularly** - Generate new tokens periodically for security
4. **Use descriptive profile names** - e.g., `prod`, `staging`, `demo`
5. **Secure config file** - Ensure proper file permissions (chmod 600)

## See Also

- [CLI Command Reference](/cli-commands)
- [API Documentation](/api/v1)
- [Configuration Guide](/configuration)
