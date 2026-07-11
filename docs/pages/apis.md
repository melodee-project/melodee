---
title: API Overview
description: Choose between Melodee's native, OpenSubsonic-compatible, and Jellyfin-compatible HTTP APIs.
permalink: /apis/
tags:
  - api
  - integration
  - clients
---

# API Overview

Melodee 2.2.0 exposes three HTTP API surfaces. They share the same users and
media library, but their routes, credentials, and compatibility goals differ.

| API | Public route | Authentication | Use it for |
|-----|--------------|----------------|------------|
| [Native API](/api/) | `/api/v1/*` | Melodee JWT bearer token | New Melodee integrations and administration |
| [OpenSubsonic](/api-opensubsonic/) | `/rest/*` | Subsonic username/token/salt or legacy password | Subsonic and OpenSubsonic clients |
| [Jellyfin compatibility](/api-jellyfin/) | Jellyfin root routes, internally `/api/jf/*` | Jellyfin access token | Music clients that speak the implemented Jellyfin subset |

Compatibility layers do not expose every endpoint implemented by their
upstream projects. Review the [OpenSubsonic compatibility matrix](/opensubsonic-matrix/)
and the Jellyfin route list before selecting a client.

## Native API

The native API is versioned under `/api/v1`. Obtain a JWT from
`POST /api/v1/auth/authenticate`, then send it as a bearer token. It covers
library browsing, search, playlists, queues, user data, requests, shares,
podcasts, charts, playback, analytics, and administrative operations.

The running server publishes native API discovery documents at:

- Scalar UI: `/scalar/v1`
- OpenAPI JSON: `/openapi/v1.json`

The OpenAPI document describes the native API; it is not a specification for
the OpenSubsonic or Jellyfin compatibility surfaces.

## OpenSubsonic API

OpenSubsonic endpoints accept GET and form-encoded POST requests at both
`/rest/endpoint` and `/rest/endpoint.view`. JSON, XML, and JSONP response modes
are supported. Melodee implements music browsing, search, streaming,
annotations, playlists, bookmarks, queues, shares, radio, scanning, jukebox,
and optional podcast endpoints. Deprecated chat, video, captions, and HLS
routes intentionally return HTTP 410.

## Jellyfin Compatibility API

Jellyfin clients connect to the Melodee server root. Middleware recognizes the
Jellyfin discovery and login paths and authenticated MediaBrowser requests,
then routes them internally beneath `/api/jf`. Support is limited to the music
and playlist endpoints documented on the [Jellyfin API page](/api-jellyfin/).
The database setting `jellyfin.enabled` controls this surface and defaults to
`true`.

## Base URL and TLS

Use the externally reachable origin only, for example
`https://music.example.com`; append the route for the selected API. Configure
`system.baseUrl` to the same public origin and terminate TLS either in Melodee
or a trusted reverse proxy. Do not expose plain HTTP credentials or access
tokens across an untrusted network.

See [Configuration](/configuration/) for proxy and base-URL settings.
