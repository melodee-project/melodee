---
title: OpenSubsonic API
description: Connect OpenSubsonic and Subsonic clients to Melodee's implemented compatibility endpoints.
permalink: /api-opensubsonic/
tags:
  - api
  - opensubsonic
  - clients
---

# OpenSubsonic API

Melodee exposes a music-focused Subsonic/OpenSubsonic compatibility API beneath
`/rest`. Both route forms are accepted:

```text
https://music.example.com/rest/getAlbum
https://music.example.com/rest/getAlbum.view
```

Most endpoints accept GET or `application/x-www-form-urlencoded` POST requests.
The [compatibility matrix](/opensubsonic-matrix/) lists the implemented and
intentionally unsupported endpoints in Melodee 2.2.0.

## Authentication

For widest client compatibility, use Subsonic token authentication:

```text
u=alice
t=md5(password + salt)
s=random-salt
v=1.16.1
c=client-name
```

For example:

```text
GET /rest/getArtists?u=alice&t=HASH&s=SALT&v=1.16.1&c=my-client&f=json
```

Melodee also accepts the legacy `p` parameter as either a clear-text password
or `enc:` followed by the password's hexadecimal bytes. This mode can expose
reusable credentials and should be avoided except for clients that provide no
token-authentication option. Always use HTTPS.

`ping` and `getOpenSubsonicExtensions` can be called without authentication.
Other routes normally authenticate the user. Local browser requests can also
use Melodee's internal cookie flow; third-party clients should not depend on
that bypass.

## Response Formats

The default response is XML. Select a format with `f`:

| Value | Result |
|-------|--------|
| `xml` or omitted | XML |
| `json` | JSON |
| `jsonp` | JSONP; also requires `callback` |

```text
GET /rest/ping?f=json&v=1.16.1&c=my-client
```

Structured responses use the standard `subsonic-response` envelope with
`status`, protocol version, server type/version, data, or an OpenSubsonic error.
Streaming and image endpoints instead return binary content and the relevant
HTTP headers.

## Implemented Areas

Melodee provides routes for:

- artists, albums, songs, genres, folders, indexes, lists, and search;
- stream, download, cover art, avatar, and lyrics retrieval;
- starring, ratings, scrobbling, playlists, bookmarks, and play queues;
- shares, internet radio, library scans, and conditional jukebox control;
- podcast subscriptions and episodes when podcasts and the user's podcast role
  are enabled;
- selected user operations (`getUser` and `createUser`).

Chat, video, HLS, and caption routes return HTTP 410. `updateUser`,
`deleteUser`, `changePassword`, and `getUsers` return HTTP 501. These explicit
responses let clients detect unsupported functionality without mistaking it
for a missing route.

## Server Extensions

`getOpenSubsonicExtensions` currently advertises:

- `melodeeExtensions`
- `apiKeyAuthentication`
- `formPost`
- `songLyrics`
- `transcodeOffset`

Clients should still negotiate individual features and handle an unsupported
response. Token-plus-salt authentication is the documented interoperable login
method for Melodee 2.2.0.

## Client Setup

In a compatible client, enter the origin of the Melodee installation, such as
`https://music.example.com`, not a native `/api/v1` URL. Select token
authentication if the client offers it and choose an OpenSubsonic/Subsonic API
version no newer than the client and server both support.

Client behavior varies by version and by which optional endpoints it assumes.
Passing login and basic browsing does not guarantee that every client feature
is implemented. Use the compatibility matrix and test streaming, seeking,
playlists, downloads, and scrobbling before relying on a client operationally.

See the upstream [OpenSubsonic API specification](https://opensubsonic.netlify.app/docs/endpoints/)
for parameter semantics; where it differs, Melodee's matrix and running
behavior take precedence.
