---
title: Podcasts
description: Subscribe to RSS podcasts, discover and import channels, download episodes, and track playback.
permalink: /podcasts/
tags:
  - podcasts
  - open-subsonic
  - administration
---

# Podcasts

Melodee provides per-user RSS podcast subscriptions, scheduled feed refreshes, episode downloads, browser playback, bookmarks, history, search, discovery, and OPML import/export.

Podcast support is enabled by default with `podcast.enabled=true`. Each user also has a **Podcast** role, enabled by default. OpenSubsonic podcast routes require both the feature and that role.

## Subscribe

Open **Podcasts** from the main navigation and choose one of these methods:

- **Add Channel** accepts an RSS or Atom feed URL.
- **Discover** searches or browses Apple's iTunes podcast directory, then subscribes using the result's feed URL.
- **OPML Import/Export** imports subscriptions from OPML XML or exports the current user's subscriptions.

New channels are refreshed immediately. Duplicate feed URLs for the same user are rejected.

Feed requests include SSRF protections. HTTPS is required unless an administrator explicitly enables `podcast.http.allowHttp`; private, loopback, link-local, and otherwise unsafe destinations are rejected.

## Manage channels

Each channel belongs to the subscribing user. From the channel page you can:

- refresh the feed;
- pin or unpin it on your dashboard;
- enable automatic downloads;
- set a custom refresh interval from 0 through 168 hours;
- limit downloaded episode count;
- limit downloaded storage in MiB;
- delete the channel.

A refresh interval of 0 uses the global refresh job. Deleting a channel normally soft-deletes its metadata and removes downloaded files. The native API exposes a `softDelete` query option for administrators and integrations that need different behavior.

## Episodes and playback

The web UI displays these download states: **None**, **Queued**, **Downloading**, **Downloaded**, and **Failed**.

- Select **Download** to queue an episode.
- The background download job writes it under `/app/podcasts/{userId}/{channelId}/`.
- The browser player is available for downloaded episodes.
- Select **Delete** to remove the local file while retaining feed metadata.
- The enclosure link opens the publisher's original media URL.

While the web player runs, Melodee:

- sends a now-playing heartbeat every 30 seconds;
- saves the resume bookmark every 10 seconds;
- records a completed play at the end, or after the player's threshold is reached;
- resumes at the stored bookmark the next time.

Bookmarks are one position plus an optional comment per user and episode. The current web dialog displays the saved position; comment editing is available through the native API.

## Search and OPML

**Search Episodes** searches titles and descriptions within the current user's channels. The web UI requires at least three characters.

OPML export returns the current user's active subscriptions. Import accepts OPML XML, adds valid feeds, skips duplicates, and reports failures. Native API import expects XML in the request body, not a JSON wrapper.

## Background jobs

| Job | Default schedule | Function |
|---|---|---|
| PodcastRefreshJob | Every 15 minutes | Refresh channels whose next-sync time is due |
| PodcastDownloadJob | Every 5 minutes | Process queued downloads |
| PodcastCleanupJob | Daily at 02:00 | Enforce enabled retention policies |
| PodcastRecoveryJob | Every 30 minutes | Reset stuck downloads and remove orphaned temporary files |

An empty job cron expression disables that job. See [Background Jobs](/jobs/) for scheduler management.

## Default limits

These are the seeded 2.2.0 values:

| Setting | Default |
|---|---:|
| `podcast.http.timeoutSeconds` | 30 seconds |
| `podcast.http.maxRedirects` | 10 |
| `podcast.http.maxFeedBytes` | 10 MiB |
| `podcast.refresh.maxItemsPerChannel` | 500 |
| `podcast.download.maxConcurrent.global` | 2 per job run |
| `podcast.download.maxConcurrent.perUser` | 1 per job run |
| `podcast.download.maxEnclosureBytes` | 2 GiB |
| `podcast.quota.maxBytesPerUser` | 5 GiB |
| `podcast.retention.downloadedEpisodesInDays` | 0 (disabled) |
| `podcast.retention.keepLastNEpisodes` | 0 (disabled) |
| `podcast.retention.keepUnplayedOnly` | false |
| `podcast.recovery.stuckDownloadThresholdMinutes` | 60 |
| `podcast.recovery.orphanedUsageThresholdHours` | 12 |

Per-channel limits are also checked before a queued download starts. A limit of 0 means unlimited or disabled, depending on the setting.

The settings named “concurrent” currently bound how many downloads the sequential job processes globally and per user during one execution; they do not create that many parallel network transfers.

## OpenSubsonic API

Melodee implements these podcast routes with GET and POST variants:

```text
/rest/getPodcasts
/rest/getNewestPodcasts
/rest/refreshPodcasts
/rest/createPodcastChannel
/rest/deletePodcastChannel
/rest/downloadPodcastEpisode
/rest/deletePodcastEpisode
/rest/streamPodcastEpisode
```

`refreshPodcasts` refreshes all channels for the authenticated user; it does not take a channel ID. Podcast IDs use Melodee's typed OpenSubsonic format.

Client podcast support varies. The server routes above are the compatibility contract; no particular third-party client's complete feature set is guaranteed.

## Native API

The podcast controller is currently excluded from generated OpenAPI output, but these authenticated routes are implemented:

```text
GET    /api/v1/podcasts/channels
POST   /api/v1/podcasts/channels
PATCH  /api/v1/podcasts/channels/{numericId}
POST   /api/v1/podcasts/channels/{numericId}/refresh
DELETE /api/v1/podcasts/channels/{numericId}
GET    /api/v1/podcasts/channels/{numericId}/episodes

POST   /api/v1/podcasts/episodes/{numericId}/download
DELETE /api/v1/podcasts/episodes/{numericId}
POST   /api/v1/podcasts/episodes/{numericId}/play?secondsPlayed=123
GET    /api/v1/podcasts/episodes/{numericId}/bookmark
PUT    /api/v1/podcasts/episodes/{numericId}/bookmark
DELETE /api/v1/podcasts/episodes/{numericId}/bookmark
GET    /api/v1/podcasts/episodes/{numericId}/history
GET    /api/v1/podcasts/episodes/search?query={text}

GET    /api/v1/podcasts/opml/export
POST   /api/v1/podcasts/opml/import
GET    /api/v1/podcasts/discover/search?query={text}
GET    /api/v1/podcasts/discover/trending
GET    /api/v1/podcasts/discover/lookup/{itunesId}
```

The `/play` route is a now-playing heartbeat. A final played submission is available through OpenSubsonic `/rest/scrobble`; the browser player records completion directly through the playback service.

## Troubleshooting

### A feed will not load

1. Confirm it is a direct RSS/Atom feed, not a show web page.
2. Prefer HTTPS.
3. Check the channel's sync error and server log.
4. If the host resolves to a private address, use a public feed endpoint; SSRF protection intentionally blocks it.

### Downloads remain queued

1. Confirm PodcastDownloadJob is enabled and running.
2. Check the per-user and per-channel storage limits.
3. Check the 2 GiB enclosure limit and the reported MIME type.
4. Inspect the episode's download error and the Podcast library permissions.

### Browser playback is unavailable

The 2.2.0 web player plays locally downloaded episodes. Queue the episode and wait for **Downloaded** status, or use an OpenSubsonic client that calls `streamPodcastEpisode`.
