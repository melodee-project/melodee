---
title: OpenSubsonic Compatibility Matrix
description: Endpoint-by-endpoint status for Melodee 2.2.0's OpenSubsonic compatibility API.
permalink: /opensubsonic-matrix/
tags:
  - opensubsonic
  - api
  - compatibility
---

# OpenSubsonic Compatibility Matrix

**Last updated:** July 11, 2026

**Applies to:** Melodee 2.2.0

This matrix is based on the routes implemented by the 2.2.0 server. Supported
means that Melodee has a handler for the endpoint; it does not promise every
optional field or every third-party client workflow.

| Status | Meaning |
|--------|---------|
| Supported | Implemented for the applicable Melodee music data |
| Conditional | Implemented but requires a feature, role, or backend |
| Gone | Intentionally unsupported; returns HTTP 410 |
| Not implemented | Known route that returns HTTP 501 |

Both the plain and `.view` route forms are registered unless noted otherwise.

## System and Browsing

| Endpoint | Status | Notes |
|----------|--------|-------|
| `ping` | Supported | Also registered at `/ping`; no authentication required |
| `getLicense` | Supported | Returns server license response |
| `getOpenSubsonicExtensions` | Supported | Public extension discovery |
| `getMusicFolders` | Supported | Maps eligible libraries to music folders |
| `getIndexes` | Supported | Indexed artist view |
| `getArtists` | Supported | ID3-oriented artist view |
| `getArtist` | Supported | Artist and album details |
| `getAlbum` | Supported | Album and song details |
| `getSong` | Supported | Song details |
| `getGenres` | Supported | Genre counts |
| `getMusicDirectory` | Supported | Directory-style browsing |
| `getAlbumInfo`, `getAlbumInfo2` | Supported | Album metadata |
| `getArtistInfo`, `getArtistInfo2` | Supported | Artist metadata and similar artists |
| `getSimilarSongs`, `getSimilarSongs2` | Supported | Similar-song results |
| `getTopSongs` | Supported | Artist top songs |
| `getVideos`, `getVideoInfo` | Gone | Melodee is audio-focused |

## Lists and Search

| Endpoint | Status | Notes |
|----------|--------|-------|
| `getAlbumList`, `getAlbumList2` | Supported | Album list modes |
| `getRandomSongs` | Supported | Random library songs |
| `getSongsByGenre` | Supported | Genre-filtered songs |
| `getNowPlaying` | Supported | Active playback data known to Melodee |
| `getStarred`, `getStarred2` | Supported | User-starred media |
| `search2`, `search3` | Supported | Artist, album, and song search |
| `searchForArtist` | Supported | Melodee extension |
| `searchForArtistImage` | Supported | Melodee extension |
| `searchForAlbumImage` | Supported | Melodee extension |

## Media and Annotation

| Endpoint | Status | Notes |
|----------|--------|-------|
| `stream` | Supported | Direct or transcoded audio; range requests supported |
| `download` | Supported | Audio download |
| `getCoverArt` | Supported | Sized cover-art requests |
| `getAvatar` | Supported | User image |
| `getLyrics` | Supported | Artist/title lookup |
| `getLyricsBySongId` | Supported | Structured lyrics extension |
| `hls`, `getCaptions` | Gone | Video-oriented/deprecated surface |
| `star`, `unstar` | Supported | Artist, album, or song annotation |
| `setRating` | Supported | User rating |
| `scrobble` | Supported | Now-playing and submission behavior |

## Playlists, Bookmarks, and Queue

| Endpoint | Status | Notes |
|----------|--------|-------|
| `getPlaylists`, `getPlaylist` | Supported | User-visible playlists |
| `createPlaylist` | Supported | Create a playlist |
| `updatePlaylist` | Supported | Metadata and song changes |
| `deletePlaylist` | Supported | Delete a playlist |
| `getBookmarks` | Supported | User bookmarks |
| `createBookmark`, `deleteBookmark` | Supported | Bookmark changes |
| `getPlayQueue`, `savePlayQueue` | Supported | User queue persistence |

## Shares, Radio, and Scanning

| Endpoint | Status | Notes |
|----------|--------|-------|
| `getShares`, `createShare`, `updateShare`, `deleteShare` | Supported | User shares |
| `getInternetRadioStations` | Supported | Internet radio listing |
| `createInternetRadioStation` | Supported | Create a station |
| `updateInternetRadioStation` | Supported | Update a station |
| `deleteInternetRadioStation` | Supported | Delete a station |
| `startScan`, `getScanStatus` | Supported | Library scan control/status |

## Jukebox and Podcasts

| Endpoint | Status | Notes |
|----------|--------|-------|
| `jukeboxControl` | Conditional | Requires `jukebox.enabled` and a configured backend; otherwise HTTP 410 |
| `getPodcasts`, `getNewestPodcasts` | Conditional | Requires podcasts enabled and the user's podcast role |
| `refreshPodcasts` | Conditional | Same feature and role checks |
| `createPodcastChannel`, `deletePodcastChannel` | Conditional | Same feature and role checks |
| `deletePodcastEpisode`, `downloadPodcastEpisode` | Conditional | Same feature and role checks |
| `streamPodcastEpisode` | Conditional | Same feature and role checks |

## Users and Deprecated Features

| Endpoint | Status | Notes |
|----------|--------|-------|
| `getUser` | Supported | Self, or another user when authorized |
| `createUser` | Supported | Administrator operation |
| `updateUser`, `deleteUser` | Not implemented | HTTP 501 |
| `changePassword`, `getUsers` | Not implemented | HTTP 501 |
| `getChatMessages`, `addChatMessage` | Gone | Deprecated chat surface; HTTP 410 |

## Advertised Extensions

`getOpenSubsonicExtensions` reports version 1 of:

| Extension | Server capability |
|-----------|-------------------|
| `melodeeExtensions` | Melodee-specific search helpers |
| `apiKeyAuthentication` | API-key extension is advertised; token-plus-salt remains the documented interoperable login flow |
| `formPost` | Form-encoded POST requests |
| `songLyrics` | Structured lyrics by song ID |
| `transcodeOffset` | Transcoding start offset |

## Verification Guidance

Compatibility is a combination of endpoint coverage, the client's assumptions,
and the media workflow. For a prospective client, test login, browse, stream,
seek, playlist editing, downloads, scrobbling, and any optional features you
need. When reporting a problem, include the client/version, endpoint, sanitized
parameters, status code, response envelope, and matching Melodee correlation or
log entry. Never include passwords, tokens, salts paired with hashes, or API
keys.

See [OpenSubsonic API](/api-opensubsonic/) for setup and authentication.
