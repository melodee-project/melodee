---
title: Shares
description: Create share records for songs, albums, playlists, or artists and understand the 2.2.0 public-access limitations.
permalink: /shares/
tags:
  - shares
  - open-subsonic
  - preview
---

# Shares

> Shares are a preview feature in 2.2.0. Creation and metadata APIs are implemented, but anonymous playback, expiry enforcement, downloads, and visit tracking are not yet consistent across the web and API surfaces.

A share stores an owner, resource type and database ID, short URL ID, description, optional expiration, downloadable flag, and visit counters.

Supported resource types in the native API are **Song**, **Album**, **Playlist**, and **Artist**. OpenSubsonic creation supports songs, albums, and playlists.

## Create and manage shares

The 2.2.0 Shares page lists records and lets an Editor or Administrator delete selected unlocked shares. It does not have create or edit forms, and the list is not filtered to the current owner.

Create or update shares through the native or OpenSubsonic API. The native API requires the user's **Share** capability. Owners can read, update, and delete their records; administrators can manage another user's record through native endpoints.

The generated URL has this form:

```text
https://your-server.example/share/{shortId}
```

## Current public-access behavior

There are two anonymous surfaces:

### Public JSON endpoint

```text
GET /api/v1/shares/public/{shortId}
```

This endpoint:

- rejects an expired share with HTTP 410;
- returns metadata for artists, albums, songs, and playlists;
- returns song stream URLs and the `isDownloadable` flag.

The returned stream URLs still point at authenticated `/rest/stream` routes. The flag does not grant anonymous download access.

### Blazor share page

```text
GET /share/{shortId}
```

The page builds a simple audio player for a song, album, or playlist. In 2.2.0:

- it does not support Artist shares;
- it does not check `ExpiresAt`;
- it does not display the description or downloadable flag;
- its audio URLs require normal OpenSubsonic authentication.

Consequently, a recipient without a Melodee account cannot reliably play the media. Do not present 2.2.0 share links as anonymous streaming links.

## Other current limitations

- `VisitCount`, `LastVisitedAt`, and `ShareActivity` are modeled but public requests do not update them.
- No share-download endpoint enforces `IsDownloadable`.
- The native and OpenSubsonic update paths persist description changes, but the current shared update service does not persist changed expiration or downloadable values.
- The web list exposes few details and no per-share statistics view.

Use an authenticated playlist or client account when reliable remote playback is required.

## Native API

Use a native bearer token:

```text
GET    /api/v1/shares?page=1&pageSize=20
GET    /api/v1/shares/{shareGuid}
POST   /api/v1/shares
PUT    /api/v1/shares/{shareGuid}
DELETE /api/v1/shares/{shareGuid}
GET    /api/v1/shares/public/{shortId}
```

Example create body:

```json
{
  "shareType": "Album",
  "resourceId": "00000000-0000-0000-0000-000000000000",
  "description": "Listen to this album",
  "isDownloadable": false,
  "expiresAt": "2026-08-01T00:00:00Z"
}
```

`resourceId` is the resource's raw API-key GUID. `expiresAt` uses extended ISO 8601.

The native list is filtered to the authenticated user's records, even though the Blazor Shares page currently is not.

## OpenSubsonic API

Melodee implements GET and POST variants:

```text
/rest/getShares
/rest/createShare?id={typedResourceId}
/rest/updateShare?id={shareId}
/rest/deleteShare?id={shareId}
```

`description` is optional. `expires` is Unix time in milliseconds. IDs returned by Melodee can be numeric or use its typed API-key form depending on the operation and client.

Because public playback is incomplete, third-party client “share” support does not by itself make the resulting URL anonymously playable.

## Security guidance

- Assume anybody who receives a short ID can read the metadata endpoint until the record is deleted or its API-enforced expiry is reached.
- Do not use shares for confidential content.
- Reverse-proxy authentication can protect the whole instance, but it also prevents anonymous share access.
- Delete obsolete records rather than relying on the web page's current expiry behavior.
- Respect licensing and redistribution rights for the media you host.
