---
title: Background Jobs
description: Understand, schedule, run, and troubleshoot Melodee's Quartz background jobs.
permalink: /jobs/
tags:
  - administration
  - jobs
  - automation
---

# Background Jobs

Melodee uses Quartz.NET for recurring import, metadata, chart, and podcast work.
At startup, each job is registered only when its database-backed cron setting is
nonempty. The host setting `QuartzDisabled=true` disables the scheduler
entirely.

Administrators can inspect jobs at **Administration > Jobs**. The page shows
the next and previous run, trigger, state, duration statistics, history, and
recent failures. It can trigger, pause, resume, or interrupt a job and can pause
or resume the whole scheduler.

## Default Schedules

These are the seed values for a new 2.2.0 database. Existing installations keep
their stored values during an upgrade.

| Job | Setting key | Seed cron | Meaning |
|-----|-------------|-----------|---------|
| `ArtistHousekeepingJob` | `jobs.artistHousekeeping.cronExpression` | `0 0 0/1 1/1 * ? *` | Hourly |
| `ArtistSearchEngineRepositoryHousekeepingJob` | `jobs.artistSearchEngineHousekeeping.cronExpression` | `0 0 0 * * ?` | Daily at 00:00 |
| `LibraryInboundProcessJob` | `jobs.libraryProcess.cronExpression` | `0 */10 * ? * *` | Every 10 minutes |
| `StagingAutoMoveJob` | `jobs.stagingAutoMove.cronExpression` | `0 */15 * * * ?` | Every 15 minutes |
| `LibraryInsertJob` | `jobs.libraryInsert.cronExpression` | `0 0 0 * * ?` | Daily at 00:00 |
| `ChartUpdateJob` | `jobs.chartUpdate.cronExpression` | `0 0 2 * * ?` | Daily at 02:00 |
| `MusicBrainzUpdateDatabaseJob` | `jobs.musicbrainzUpdateDatabase.cronExpression` | `0 0 12 1 * ?` | First day of each month at 12:00 |
| `PodcastRefreshJob` | `jobs.podcastRefresh.cronExpression` | `0 */15 * ? * *` | Every 15 minutes |
| `PodcastDownloadJob` | `jobs.podcastDownload.cronExpression` | `0 */5 * ? * *` | Every 5 minutes |
| `PodcastCleanupJob` | `jobs.podcastCleanup.cronExpression` | `0 0 2 * * ?` | Daily at 02:00 |
| `PodcastRecoveryJob` | `jobs.podcastRecovery.cronExpression` | `0 */30 * ? * *` | Every 30 minutes |

The server recognizes settings for `NowPlayingCleanupJob` and
`StagingAlbumRevalidationJob`, but a new 2.2.0 database does not seed cron values
for them. They are therefore unscheduled unless the administrator creates
nonempty values for:

- `jobs.nowPlayingCleanup.cronExpression`
- `jobs.stagingAlbumRevalidation.cronExpression`

Cron expressions use Quartz syntax, including a seconds field. Times are
evaluated in the scheduler's effective time zone. Verify the displayed next-fire
time after changing a schedule, especially when the container's `TZ` setting or
daylight-saving rules matter.

## Media Ingestion Pipeline

The scheduled ingestion stages can chain when they actually process data:

```text
Inbound processing -> move valid staging albums -> insert storage metadata
```

1. `LibraryInboundProcessJob` scans the unlocked Inbound library, parses and
   validates media, writes `melodee.json`, and places processed albums in
   Staging. Scheduled runs skip work when the directory has not changed.
2. `StagingAutoMoveJob` moves or merges albums whose status is `Ok` into the
   first unlocked Storage library.
3. `LibraryInsertJob` reads storage `melodee.json` files and updates PostgreSQL,
   making the media available to clients.

A cron-triggered stage triggers the next scheduled job only after handling data.
A normal manual trigger does not chain unless the caller explicitly supplies the
internal chain flag. The standalone `mcli library scan` command is the clearest
way to request the full workflow manually.

`StagingAlbumRevalidationJob`, when separately scheduled, revisits Staging
albums whose artists were invalid or unknown and retries metadata lookup. It is
not itself part of the chain.

## Maintenance and Data Jobs

| Job | Work performed |
|-----|----------------|
| `ArtistHousekeepingJob` | Finds eligible artists without images, searches configured image providers, validates a result, and updates the artist image state |
| `ArtistSearchEngineRepositoryHousekeepingJob` | Refreshes stale artist/album records in the local search database; locked artists are skipped |
| `ChartUpdateJob` | Links unlinked chart entries to library albums while preserving manual links |
| `MusicBrainzUpdateDatabaseJob` | Downloads MusicBrainz dumps and rebuilds the local DecentDB lookup database |
| `NowPlayingCleanupJob` | Removes stale now-playing rows that no longer receive updates |

The MusicBrainz job is large and can run for hours. It requires the MusicBrainz
search engine and storage path, downloads multi-gigabyte archives, and needs
substantial temporary disk space. Do not overlap it with a backup or DecentDB
migration. See [Hardware and Sizing](/hardware/) and [DecentDB Usage &
Migration](/decentdb/).

## Podcast Jobs

Podcast jobs are scheduled by default even when `podcast.enabled` is false;
their implementations check feature and policy state before doing applicable
work.

| Job | Work performed |
|-----|----------------|
| `PodcastRefreshJob` | Fetches due RSS/Atom feeds and updates channels and episodes |
| `PodcastDownloadJob` | Downloads queued episodes within concurrency, size, and per-user quota limits |
| `PodcastCleanupJob` | Applies configured age, count, and played-state retention policies |
| `PodcastRecoveryJob` | Recovers stuck downloads and orphaned temporary usage records |

See [Podcasts](/podcasts/) for the feature, storage, quota, and retention
settings that control these jobs.

## Change a Schedule

Cron values are database-backed settings. Use the administration settings page
or `mcli`:

```bash
mcli configuration get jobs.libraryProcess.cronExpression --raw
mcli configuration set jobs.libraryProcess.cronExpression '0 */20 * ? * *'
```

Set an existing cron value to an empty string to prevent registration, or remove
a custom unseeded job setting. Restart Melodee after schedule changes because
the startup scheduler does not automatically rebuild its triggers when a
database setting changes.

Before changing a production schedule:

1. Confirm the previous run duration in **Administration > Jobs**.
2. Leave enough time to avoid overlap and downstream resource contention.
3. Check storage capacity and external-provider rate limits.
4. Restart, then verify the cron and next-fire time shown in the UI.

Jobs that mutate the same files are generally protected against concurrent
execution, but aggressive schedules can still queue work and contend for
PostgreSQL, disk, network, or external services.

## Run Jobs Manually

The administration page can trigger any currently scheduled job. The CLI can
also inspect and run jobs locally:

```bash
mcli job list
mcli job run --job ChartUpdateJob
mcli job musicbrainz-update
```

See [CLI Job Commands](/cli/job/) for exact options. An ad hoc CLI process is
outside the web server's Quartz trigger state, so confirm that the same job is
not already running before starting expensive work.

## Troubleshooting

If no jobs appear, check `QuartzDisabled`, verify that the relevant cron values
are present and nonempty, and review startup logs for invalid Quartz
expressions. If a job is present but idle, check that the scheduler and job are
not paused and inspect its next-fire time.

For a failing job, expand its row and correlate the history timestamp with
application logs. Typical causes are locked or unreadable libraries, insufficient
disk space, invalid metadata, unreachable providers, missing FFmpeg/tools,
database connectivity, disabled features, or quota limits. Correct the cause,
then trigger the job once and confirm its history before restoring an aggressive
schedule.
