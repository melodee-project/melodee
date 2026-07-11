---
title: Playlists
description: Create, import, play, and export regular playlists, and understand Melodee's file-defined dynamic playlists.
permalink: /playlists/
tags:
  - playlists
  - open-subsonic
  - administration
---

# Playlists

Melodee 2.2.0 has two playlist systems:

- **regular playlists**, stored in PostgreSQL and owned by a user;
- **file-defined dynamic playlists**, stored as trusted JSON definitions under the Playlist library.

The native smart-playlist API is a separate preview surface. It stores MQL definitions, but evaluation currently returns no media; see [MQL](/mql/#smart-playlist-preview).

## Regular playlists

Regular playlists preserve a manually chosen song order. They have a name, optional comment, owner, public flag, songs, and optional image.

### What the web UI supports

Open **Playlists** from the main navigation. The page can:

- list and search regular and visible dynamic playlists;
- import an M3U or M3U8 file;
- delete selected regular playlists when the current user is allowed;
- open, pin, play, and export a playlist as M3U8.

The detail page's **Lock**, **Unlock**, and **Set Cover Image** actions are placeholders in 2.2.0. The list's edit link also has no matching editor route. Use a compatible OpenSubsonic client or the native API to create, rename, add, remove, or reorder songs.

Playlist import and deletion controls are shown to Editor and Administrator users. API operations also require the relevant user capability.

### Import M3U/M3U8

Select **Import Playlist**, choose a `.m3u` or `.m3u8` file, and optionally provide a name. Melodee tries to match each media reference to a local song. Matched songs are added immediately; unmatched references are retained in import records for later inspection.

For the most portable result, use UTF-8 and paths shaped like:

```text
#EXTM3U
#EXTINF:243,Artist - Song title
Artist/Album/01 - Song title.flac
```

The exported M3U8 format uses `Artist/Album/Filename`, which is also understood by the importer.

### Visibility

`IsPublic` is stored on regular playlists and returned to API clients. In the 2.2.0 Blazor playlist list, regular playlists are not filtered by owner or public status, so do not treat a private playlist as hidden from other authenticated web users.

Owners can modify their playlists through the APIs. Administrators can delete other users' playlists; normal users cannot.

## File-defined dynamic playlists

Dynamic playlists execute a PostgreSQL `WHERE` and optional `ORDER BY` fragment against the song catalog every time the playlist is read. They live under:

```text
/app/playlists/dynamic/*.json
```

The administrator/editor import dialog accepts this shape:

```json
{
  "id": "1f631cc9-c7ac-4f23-8315-092c1c2db57e",
  "isEnabled": true,
  "name": "Recently added",
  "comment": "Songs added during the last 30 days",
  "isPublic": true,
  "forUserId": null,
  "songSelectionWhere": "s.\"CreatedAt\" > NOW() - INTERVAL '30 days'",
  "songSelectionOrder": "s.\"CreatedAt\" DESC",
  "songLimit": 100
}
```

`id`, `name`, `comment`, and `songSelectionWhere` are required by the importer. Set `forUserId` to a user's API-key GUID for a user-specific definition; otherwise a definition must be public to appear.

Dynamic definitions are disabled globally when `playlist.dynamicPlaylist.disabled` is `true`.

> Dynamic playlist SQL is interpolated into database queries. Only a trusted administrator should write or import these files. Do not accept definitions from untrusted users.

`songLimit` is part of the definition model, but the current query path applies API paging rather than enforcing that value consistently.

## OpenSubsonic API

Melodee implements the standard playlist routes using GET or POST:

```text
/rest/getPlaylists
/rest/getPlaylist?id={playlistId}
/rest/createPlaylist?name={name}
/rest/updatePlaylist?playlistId={id}
/rest/deletePlaylist?id={playlistId}
```

`updatePlaylist` supports metadata updates plus song additions and removals using the standard OpenSubsonic parameters. IDs returned by Melodee normally use a typed prefix such as `playlist|{guid}`.

Client menus and capabilities vary, so verify create/reorder/public controls in the client you use rather than relying on a client-specific workflow.

## Native API

Use a native bearer token:

```text
GET    /api/v1/playlists?page=1&pageSize=20
GET    /api/v1/playlists/{playlistGuid}
GET    /api/v1/playlists/{playlistGuid}/songs
POST   /api/v1/playlists
POST   /api/v1/playlists/import
PUT    /api/v1/playlists/{playlistGuid}
DELETE /api/v1/playlists/{playlistGuid}
POST   /api/v1/playlists/{playlistGuid}/songs
DELETE /api/v1/playlists/{playlistGuid}/songs
PUT    /api/v1/playlists/{playlistGuid}/songs/reorder
POST   /api/v1/playlists/{playlistGuid}/image
DELETE /api/v1/playlists/{playlistGuid}/image
```

The native smart-playlist preview is under `/api/v1/playlists/smart`. See [Native API](/api/) for token and response details.
