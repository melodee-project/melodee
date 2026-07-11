---
title: Scrobbling
description: Record now-playing and completed-play events locally and optionally forward them to Last.fm.
permalink: /scrobbling/
tags:
  - scrobbling
  - open-subsonic
  - lastfm
---

# Scrobbling

Scrobbling records playback activity. Melodee distinguishes a **now-playing** heartbeat from a completed **submission**.

## What Melodee records

A now-playing event:

- clears the current user's previous now-playing item;
- creates or updates a play-history row with progress, client, user agent, and IP address;
- appears on the shared **Now Playing** page;
- can update Last.fm's now-playing state.

A completed submission:

- increments artist, album, song, and per-user song play counts;
- updates last-played timestamps;
- converts the matching now-playing row into completed history, or creates completed history;
- can submit the play to Last.fm.

Melodee does not decide whether a listener heard 50% or four minutes before accepting a completed music submission. The client decides when to send `submission=true`. The native Played route records the full catalog duration as seconds played.

## Enable a user

The native scrobble route requires the user's **Scrobbling** capability (`IsScrobblingEnabled`). Administrators manage this on the user record.

OpenSubsonic authentication and the client's own scrobble setting also apply. Client menus vary; look for an option such as “scrobble to server” or “submit plays.”

## OpenSubsonic API

`/rest/scrobble` accepts GET or POST:

```text
/rest/scrobble?id={songId}&submission=false
/rest/scrobble?id={songId}&submission=true
```

Parameters:

| Parameter | Meaning |
|---|---|
| `id` | One or more typed Melodee song or podcast-episode IDs |
| `submission` | `false` for now playing; `true` for completed, which is the default |
| `time` | Optional values paired with the IDs |

For music, Melodee suppresses a duplicate OpenSubsonic completed submission when it cannot find a corresponding now-playing record. If counts do not change, confirm the client sends `submission=false` when playback starts and `submission=true` when it completes.

Podcast episode IDs are routed to podcast playback history and bookmarks rather than music play counts.

## Native API

Use a native bearer token with the Scrobbling capability:

```text
POST /api/v1/scrobble
```

```json
{
  "songId": "00000000-0000-0000-0000-000000000000",
  "playerName": "Example Player",
  "scrobbleType": "NowPlaying",
  "timestamp": null,
  "playedDuration": 75
}
```

Set `scrobbleType` to `Played` for a completed submission. The route accepts raw song API-key GUIDs, not the typed OpenSubsonic ID.

## Last.fm

Last.fm is the only external scrobbler implemented in 2.2.0. Libre.fm and ListenBrainz forwarding are not implemented.

An administrator must configure:

| Setting | Purpose |
|---|---|
| `scrobbling.lastFm.Enabled` | Enable the Last.fm plug-in |
| `scrobbling.lastFm.apiKey` | Last.fm application API key |
| `scrobbling.lastFm.sharedSecret` | Last.fm application shared secret |

Each user then needs a Last.fm session key. There is no account-linking control in the 2.2.0 Blazor profile page; integrations can complete the flow with:

```text
GET  /api/v1/scrobble/lastfm/auth-url?callback={absoluteHttpOrHttpsUrl}
POST /api/v1/scrobble/lastfm/session
POST /api/v1/scrobble/lastfm/disconnect
```

The session request body is `{ "token": "lastfm-token" }`. If Last.fm reports that the session is no longer authenticated, Melodee clears the user's stored session key.

## Privacy and retention

Now-playing and completed history store the user, media, client name, user agent, IP address, and playback progress. The Now Playing page is instance-wide; 2.2.0 has no per-user visibility switch.

The public web UI does not provide per-user history export or deletion. Apply your own database retention policy if local privacy requirements call for one, and disclose external Last.fm forwarding to users.

## Troubleshooting

### Now Playing is empty

1. Confirm the user's Scrobbling capability is enabled.
2. Confirm the client calls `/rest/scrobble` with `submission=false`.
3. Check authentication and typed song IDs.
4. Review the server log and **NowPlayingCleanupJob** history.

### Counts do not increase

1. Confirm a completed `submission=true` request follows now playing.
2. Check that the song still exists.
3. Check for client-side minimum-listen rules.
4. Avoid two clients submitting the same play.

### Last.fm does not update

1. Confirm all three server settings above.
2. Confirm the user completed the session exchange.
3. Re-link after a revoked or expired Last.fm session.
4. Review Last.fm plug-in errors in the server log.
