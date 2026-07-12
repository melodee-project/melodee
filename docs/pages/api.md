---
title: Melodee Native API
description: Authenticate with Melodee and integrate with its versioned native REST API.
permalink: /api/
tags:
  - api
  - jwt
  - integration
---

# Melodee Native API

The native REST API is versioned beneath `/api/v1`. The running application
provides the authoritative request and response schemas:

- Scalar UI: `/scalar/v1`
- OpenAPI JSON: `/openapi/v1.json`

Use those resources for generated clients and exact parameters. This page
covers authentication, conventions, and route families.

## Authentication

Most native endpoints require a short-lived JWT, not a user's persistent GUID
API key. Authenticate with either `userName` or `email` plus a password:

```http
POST /api/v1/auth/authenticate
Content-Type: application/json

{
  "userName": "alice",
  "email": null,
  "password": "replace-me"
}
```

The response includes `user`, `serverVersion`, `token`, `expiresAt`,
`refreshToken`, and `refreshTokenExpiresAt`. Send the `token` on protected
requests:

```http
Authorization: Bearer eyJ...
```

The default access-token lifetime is 15 minutes and the default refresh-token
lifetime is 30 days. Deployments can override both. Refresh tokens rotate:

```http
POST /api/v1/auth/refresh-token
Content-Type: application/json

{
  "refreshToken": "token-from-the-previous-response",
  "deviceId": "optional-stable-device-id"
}
```

Store access and refresh tokens as secrets. Do not put them in URLs, logs,
source control, or browser storage accessible to untrusted scripts. Use HTTPS
outside an isolated loopback connection.

## Public and Special Authentication Routes

`GET /api/v1/system/info` is public. Public shares use
`/api/v1/shares/public/{shareUniqueId}`. Browser sign-in and Google sign-in have
dedicated cookie endpoints under `/api/v1/auth`; consult OpenAPI for their
current request models.

Song URLs returned by the API may use the non-versioned route
`/song/stream/{songApiKey}/{userApiKey}/{authToken}`. That route accepts a
short-lived, base64-encoded HMAC token bound to the user, song, and client
address. Consumers should use a stream URL supplied by Melodee rather than try
to manufacture one from a JWT or user API key.

## Route Families

| Route | Scope |
|-------|-------|
| `/api/v1/albums`, `/artists`, `/songs`, `/genres` | Library browsing and user annotations |
| `/api/v1/search`, `/recommendations`, `/charts` | Search and discovery |
| `/api/v1/artist-lookup`, `/audio` | External artist lookup and audio features |
| `/api/v1/playlists`, `/playlists/smart`, `/queue` | Playlists and the user's play queue |
| `/api/v1/user`, `/user/stats` | Current-user profile, activity, and statistics |
| `/api/v1/admin` | Administrator user operations |
| `/api/v1/requests`, `/shares` | Requests, comments, and shares |
| `/api/v1/podcasts` | Channels, episodes, discovery, bookmarks, and OPML |
| `/api/v1/scrobble`, `/analytics` | Playback reporting and listening analytics |
| `/api/v1/equalizer/presets`, `/playback/settings` | Playback preferences |
| `/api/v1/playback-backend` | Playback backend registration and status |
| `/api/v1/party-sessions`, `/party-endpoints`, `/endpoints` | Party sessions, queues, endpoints, playback, and moderation |
| `/api/v1/themes` | Theme listing, selection, import, export, and administration |
| `/api/v1/auth`, `/system` | Authentication and server information |

Capabilities and administrator policies apply in addition to authentication.
A valid user token can therefore receive HTTP 403 for an operation the account
is not allowed to perform.

## Pagination and Caching

List endpoints generally return a `meta` object and a `data` collection:

```json
{
  "meta": {
    "totalCount": 100,
    "pageSize": 25,
    "page": 1,
    "totalPages": 4
  },
  "data": []
}
```

Parameter names and response models vary by resource; do not assume every list
accepts the same sort fields. Some reads expose ETags and honor conditional
request headers. Use the generated OpenAPI document for the selected endpoint.

## Errors and Limits

API errors include a machine-readable code or message and can include a
correlation ID for log lookup. Common status codes are:

| Status | Meaning |
|--------|---------|
| `400` | Request validation failed |
| `401` | JWT is missing, invalid, or expired |
| `403` | Account or capability policy denied the operation |
| `404` | Resource was not found |
| `409` | Resource state conflicts with the request |
| `412` | An ETag precondition failed |
| `429` | API, authentication, or streaming limit was exceeded |

Clients should honor `Retry-After` when present, refresh an expired access token
only once, and preserve the server's correlation ID in diagnostics.

See [API Overview](/apis/) for the compatibility APIs and [CLI Remote Server
Mode](/cli-remote-mode/) for the native operations available through `mcli`.
