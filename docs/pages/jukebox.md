---
title: Jukebox
description: Configure MPV or MPD for server-side playback and control Melodee's shared Jukebox queue.
permalink: /jukebox/
tags:
  - jukebox
  - playback
  - administration
---

# Jukebox

Jukebox plays audio through a backend associated with the Melodee server rather
than through the requesting browser or mobile client. Melodee supports an MPV
process or an MPD server and exposes controls in the web UI, native API, and
OpenSubsonic `jukeboxControl` endpoint.

Jukebox is disabled by default and has no default backend.

## Before Enabling It

The backend needs access to an audio output and to the same media paths stored by
Melodee. The effective Melodee service/container user must also be allowed to
open the audio device or connect to MPD.

The published Melodee container includes FFmpeg but does not include MPV or MPD.
For a container deployment, either:

- build a derived Melodee image that installs MPV and pass the required host
  audio device/socket into the container; or
- run MPD separately, make its TCP port reachable from Melodee, and mount the
  media at paths MPD can resolve.

Installing a package interactively inside a running container is lost when the
container is replaced. Also remember that `localhost` from inside the Melodee
container means that container, not the Docker host or another service.

For a direct host installation, install and test the chosen backend as the
account that runs Melodee:

```bash
command -v mpv
mpv --audio-device=help

# Or verify an MPD listener
nc -zv mpd.example.internal 6600
```

## Settings

Change database-backed settings in **Administration > Settings** or with
[`mcli configuration`](/cli/configuration/). The exact 2.2.0 keys are shown
below; they do not use a `jukebox.mpv.*` or `jukebox.mpd.*` prefix.

### General

| Setting | Seed value | Purpose |
|---------|------------|---------|
| `jukebox.enabled` | `false` | Show and enable Jukebox behavior |
| `jukebox.backendType` | empty | Backend name: `mpv` or `mpd` |

### MPV

| Setting | Seed value | Purpose |
|---------|------------|---------|
| `mpv.path` | empty | Executable path; empty searches `PATH` for `mpv` |
| `mpv.audioDevice` | empty | MPV audio-device value; empty uses its default |
| `mpv.extraArgs` | empty | Additional MPV arguments |
| `mpv.socketPath` | empty | IPC socket; empty creates a temporary socket path |
| `mpv.initialVolume` | `0.8` | Initial gain from `0.0` to `1.0` |
| `mpv.enableDebugOutput` | `false` | Log verbose MPV output |

MPV is started with idle mode and an IPC socket. If `mpv.socketPath` is set, its
parent directory must exist and be writable by Melodee. Use `mpv.extraArgs`
sparingly and treat it as privileged configuration.

### MPD

| Setting | Seed value | Purpose |
|---------|------------|---------|
| `mpd.instanceName` | empty | Optional display/instance name |
| `mpd.host` | `localhost` | MPD hostname or IP address |
| `mpd.port` | `6600` | MPD TCP port |
| `mpd.password` | empty | Optional MPD password |
| `mpd.timeoutMs` | `10000` | TCP/command timeout in milliseconds |
| `mpd.initialVolume` | `0.8` | Initial gain from `0.0` to `1.0` |
| `mpd.enableDebugOutput` | `false` | Log MPD command diagnostics |

Melodee sends the song's configured filesystem path to MPD. A remote or
sidecar MPD therefore needs that same path mounted and accepted by its music
configuration; network reachability alone is insufficient.

## Enable and Test

For MPV, for example:

```bash
mcli configuration set mpv.path /usr/bin/mpv
mcli configuration set mpv.initialVolume 0.5
mcli configuration set jukebox.backendType mpv
mcli configuration set jukebox.enabled true
```

For MPD:

```bash
mcli configuration set mpd.host mpd
mcli configuration set mpd.port 6600
mcli configuration set jukebox.backendType mpd
mcli configuration set jukebox.enabled true
```

Restart Melodee after changing the backend type or connection settings so a
cached backend is recreated. Then open `/jukebox`. The page initializes the
backend, displays connection state and capabilities, and refreshes status and
the queue.

The web page lets signed-in users view status and the queue. Its playback,
volume, shuffle, clear, and remove controls are currently displayed only to
administrators. When enabled, album detail pages can add songs to the shared
Jukebox queue.

## OpenSubsonic Control

Authenticated OpenSubsonic clients call:

```text
/rest/jukeboxControl?action=ACTION
```

| Action | Relevant parameters | Result |
|--------|---------------------|--------|
| `status` | none | Playback state and gain |
| `get` | none | Queue and status |
| `set` | `gain` (`0.0`-`1.0`) | Set gain |
| `start` | none | Start or resume playback |
| `stop` | none | Stop/pause current playback state |
| `skip` | `index`, `offset` | Select a queue item and optional offset |
| `add` | `id` or `ids` | Add one or more song IDs |
| `clear` | none | Empty the queue |
| `remove` | `index` | Remove a queue item |
| `shuffle` | none | Shuffle the queue |

When Jukebox is disabled or `jukebox.backendType` is empty, this endpoint
returns HTTP 410. Client support for the optional Subsonic jukebox feature
varies by client and version; verify it rather than relying on a generic
compatibility claim.

## Troubleshooting

If the page reports a disconnected backend:

1. Confirm both `jukebox.enabled` and `jukebox.backendType` in effective
   database settings.
2. Run `mcli doctor` and inspect application logs for
   `PlaybackBackendService`, `MpvPlaybackBackend`, or `MpdPlaybackBackend`.
3. Test the executable or MPD TCP connection from the Melodee runtime, not only
   from the Docker host.
4. Verify the Melodee runtime can read a song path and that MPD sees the same
   path if it is a separate process.
5. Check container audio-device/socket mounts, Linux group permissions, system
   mute state, and the configured MPV device.
6. For MPV IPC errors, verify the socket directory and remove a stale configured
   socket only after confirming no MPV process uses it.

Enable backend debug output temporarily for diagnosis; it can be noisy. Never
publish `mpd.password` or logs containing private network details.

Jukebox can register its backend as a Party endpoint, but Party Mode remains a
separate workflow. See [Party Mode](/party-mode/) for its current 2.2.0 status.
