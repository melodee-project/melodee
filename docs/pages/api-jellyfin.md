---
title: Jellyfin Compatibility API
description: Connect music clients to Melodee's implemented Jellyfin-compatible routes.
permalink: /api-jellyfin/
tags:
  - api
  - jellyfin
  - clients
---

# Jellyfin Compatibility API

Melodee 2.2.0 implements a music-focused subset of Jellyfin's HTTP API. It is a
compatibility layer, not a complete Jellyfin server.

The database setting `jellyfin.enabled` controls the API and defaults to
`true`. When disabled, explicitly prefixed compatibility routes return HTTP 404
and normal Jellyfin route rewriting is not performed.

## Routing

Jellyfin clients connect to the Melodee origin, for example:

```text
https://music.example.com
```

Melodee recognizes these unauthenticated discovery/login paths:

- `/System/Info/Public`
- `/System/Ping`
- `/Users/Public`
- `/Users/AuthenticateByName`

For later requests, MediaBrowser authorization headers, Jellyfin token headers,
or an `api_key` on a known Jellyfin path trigger routing to the internal
`/api/jf` controllers. Direct integrations may use the explicit `/api/jf`
prefix, but ordinary Jellyfin clients should use the server root they expect.

## Authentication

Authenticate with the Jellyfin request shape:

```http
POST /Users/AuthenticateByName
Content-Type: application/json
X-Emby-Authorization: MediaBrowser Client="MyClient", Device="MyDevice", DeviceId="stable-device-id", Version="1.0"

{
  "Username": "alice",
  "Pw": "replace-me"
}
```

The response includes `AccessToken`, `User`, `ServerId`, and `SessionInfo`.
Supply the returned token on subsequent requests, using the convention expected
by the client, for example:

```http
Authorization: MediaBrowser Token="TOKEN", Client="MyClient", Device="MyDevice", DeviceId="stable-device-id", Version="1.0"
```

Melodee also parses `X-MediaBrowser-Token`, `X-Emby-Token`, and `api_key`.
Jellyfin access tokens are separate from native Melodee JWTs and OpenSubsonic
credentials. By default they expire after 168 hours, and at most 10 active
tokens are retained per user; both values are configurable. `POST
/Sessions/Logout` revokes the active token.

## Implemented Route Groups

All paths below are the client-visible root form. The equivalent explicit form
adds `/api/jf` before the path.

| Area | Routes |
|------|--------|
| Discovery | `/`, `/System/Info/Public`, `/System/Ping`, `/System/Info` |
| Users | `/Users/Public`, `/Users/AuthenticateByName`, `/Users/Me`, `/Users`, `/Users/{userId}` |
| User library | `/Users/{userId}/Views`, `/Users/{userId}/Items`, `/Users/{userId}/Items/{itemId}` |
| User state | `/Users/{userId}/FavoriteItems/{itemId}`, `/Users/{userId}/PlayedItems/{itemId}` |
| Views and items | `/UserViews`, `/Items`, `/Items/{itemId}`, playback info, filters, similar items, instant mixes, files, and downloads |
| Artists and genres | `/Artists`, `/Artists/AlbumArtists`, artist detail/similar routes, `/Genres`, `/MusicGenres` |
| Songs and audio | `/Songs/{itemId}/InstantMix`, `/Audio/{itemId}/stream`, `/Audio/{itemId}/stream.{extension}`, `/Audio/{itemId}/universal`, lyrics |
| Images | Item and artist image routes beneath `/Items/*/Images` and `/Artists/*/Images` |
| Playlists | List, detail, create, update, delete, item add/remove/reorder routes beneath `/Playlists` |
| Sessions | Playing, progress, stopped, ping, capabilities, list, and logout beneath `/Sessions` |

The route may accept only the music-relevant portion of an upstream request,
and response fields that have no Melodee equivalent can be empty or omitted.
Video, television, books, plugins, Live TV, server administration, and the rest
of the full Jellyfin API are outside this compatibility surface.

## Streaming

Audio routes support direct streaming and universal streaming. The universal
route evaluates container and bitrate options and can transcode through the
configured FFmpeg executable. Range requests are supported where applicable.
The actual source format, requested parameters, and server configuration
determine whether playback is direct or transcoded.

## Client Compatibility

A client must tolerate a music-only library and absent non-music endpoints.
Different client releases probe different Jellyfin routes, so compatibility can
change independently of Melodee. Before standardizing on a client, verify:

1. discovery and password login;
2. artist, album, and playlist browsing;
3. direct play, seeking, and any required transcode format;
4. favorites and playback reporting;
5. playlist changes and downloads, if required.

Do not assume a client is supported solely because it can log in. Capture the
failed HTTP route and response when reporting a compatibility issue.

See [API Overview](/apis/) for Melodee's other API surfaces and
[Configuration Reference](/configuration-reference/) for database-backed
settings.
